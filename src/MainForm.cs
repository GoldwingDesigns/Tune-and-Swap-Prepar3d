using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LockheedMartin.Prepar3D.SimConnect;

namespace TuneAndSwap
{
    public class MainForm : Form
    {
        private const int WM_USER_SIMCONNECT = 0x0402;

        private enum EVENTS
        {
            COM1_STBY_SET,
            COM2_STBY_SET,
            COM1_SWAP,
            COM2_SWAP,
            XPNDR_SET,
        }

        private enum GROUPS { RADIO }
        private enum DEFINITIONS { RadioState }
        private enum REQUESTS { RadioState }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        private struct RadioState
        {
            public double Com1Active;
            public double Com1Standby;
            public double Com2Active;
            public double Com2Standby;
            public int Transponder; // BCO16: 0x1200 = squawk 1200
        }

        private SimConnect sim;

        private readonly Button connectButton = new Button { Text = "Connect", Width = 90 };
        private readonly Label statusLabel = new Label { Text = "Disconnected", AutoSize = true };
        private readonly CheckBox legacyBcd = new CheckBox { Text = "Use legacy 16-bit BCD (10 kHz steps)", AutoSize = true };
        private readonly Label com1Active = NewReadout();
        private readonly Label com1Standby = NewReadout();
        private readonly Label com2Active = NewReadout();
        private readonly Label com2Standby = NewReadout();
        private readonly TextBox com1Input = new TextBox { Text = "118.275", Width = 80 };
        private readonly TextBox com2Input = new TextBox { Text = "121.900", Width = 80 };
        private readonly Button com1Set = new Button { Text = "Set standby" };
        private readonly Button com2Set = new Button { Text = "Set standby" };
        private readonly Label xpndrCode = NewReadout();
        private readonly TextBox xpndrInput = new TextBox { Text = "1200", Width = 80, MaxLength = 4 };
        private readonly Button xpndrSet = new Button { Text = "Set squawk" };
        private readonly Button com1Swap = new Button { Text = "Swap" };
        private readonly Button com2Swap = new Button { Text = "Swap" };

        public MainForm()
        {
            Text = "Prepar3D v5 - COM Tune & Swap PoC";
            ClientSize = new System.Drawing.Size(520, 260);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 6,
                Padding = new Padding(10),
            };
            Controls.Add(grid);

            grid.Controls.Add(connectButton, 0, 0);
            grid.Controls.Add(statusLabel, 1, 0);
            grid.SetColumnSpan(statusLabel, 5);

            AddHeader(grid, "Radio", "Active", "Standby", "New standby (MHz)", "", "", 1);
            AddRow(grid, 2, "COM 1", com1Active, com1Standby, com1Input, com1Set, com1Swap);
            AddRow(grid, 3, "COM 2", com2Active, com2Standby, com2Input, com2Set, com2Swap);

            grid.Controls.Add(new Label { Text = "XPDR", AutoSize = true }, 0, 4);
            grid.Controls.Add(xpndrCode, 1, 4);
            grid.Controls.Add(xpndrInput, 3, 4);
            grid.Controls.Add(xpndrSet, 4, 4);

            grid.Controls.Add(legacyBcd, 0, 5);
            grid.SetColumnSpan(legacyBcd, 6);

            connectButton.Click += (s, e) => { if (sim == null) Connect(); else Disconnect(); };
            com1Set.Click += (s, e) => SetStandby(EVENTS.COM1_STBY_SET, com1Input);
            com2Set.Click += (s, e) => SetStandby(EVENTS.COM2_STBY_SET, com2Input);
            xpndrSet.Click += (s, e) => SetTransponder();
            com1Swap.Click += (s, e) => Send(EVENTS.COM1_SWAP, 0);
            com2Swap.Click += (s, e) => Send(EVENTS.COM2_SWAP, 0);

            SetControlsEnabled(false);
        }

        private static Label NewReadout() =>
            new Label { Text = "---.---", Font = new System.Drawing.Font("Consolas", 14f), AutoSize = true };

        private static void AddHeader(TableLayoutPanel g, string a, string b, string c, string d, string e, string f, int row)
        {
            string[] h = { a, b, c, d, e, f };
            for (int i = 0; i < h.Length; i++)
                g.Controls.Add(new Label { Text = h[i], AutoSize = true, Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) }, i, row);
        }

        private static void AddRow(TableLayoutPanel g, int row, string name, Control active, Control standby, Control input, Control set, Control swap)
        {
            g.Controls.Add(new Label { Text = name, AutoSize = true }, 0, row);
            g.Controls.Add(active, 1, row);
            g.Controls.Add(standby, 2, row);
            g.Controls.Add(input, 3, row);
            g.Controls.Add(set, 4, row);
            g.Controls.Add(swap, 5, row);
        }

        private void SetControlsEnabled(bool on)
        {
            xpndrSet.Enabled = com1Set.Enabled = com2Set.Enabled = com1Swap.Enabled = com2Swap.Enabled = on;
        }

        private void Connect()
        {
            try
            {
                sim = new SimConnect("Tune and Swap PoC", Handle, WM_USER_SIMCONNECT, null, 0);

                sim.OnRecvOpen += (s, d) => SetStatus("Connected to " + d.szApplicationName);
                sim.OnRecvQuit += (s, d) => Disconnect();
                sim.OnRecvException += (s, d) => SetStatus("SimConnect exception: " + (SIMCONNECT_EXCEPTION)d.dwException);
                sim.OnRecvSimobjectData += OnSimObjectData;

                // Client events -> sim events.
                sim.MapClientEventToSimEvent(EVENTS.COM1_STBY_SET, "COM_STBY_RADIO_SET");
                sim.MapClientEventToSimEvent(EVENTS.COM2_STBY_SET, "COM2_STBY_RADIO_SET");
                sim.MapClientEventToSimEvent(EVENTS.XPNDR_SET, "XPNDR_SET");
                sim.MapClientEventToSimEvent(EVENTS.COM1_SWAP, "COM_STBY_RADIO_SWAP");
                sim.MapClientEventToSimEvent(EVENTS.COM2_SWAP, "COM2_RADIO_SWAP");
                foreach (EVENTS ev in Enum.GetValues(typeof(EVENTS)))
                    sim.AddClientEventToNotificationGroup(GROUPS.RADIO, ev, false);
                sim.SetNotificationGroupPriority(GROUPS.RADIO, SimConnect.SIMCONNECT_GROUP_PRIORITY_HIGHEST);

                // Readback of the current frequencies.
                sim.AddToDataDefinition(DEFINITIONS.RadioState, "COM ACTIVE FREQUENCY:1", "MHz", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                sim.AddToDataDefinition(DEFINITIONS.RadioState, "COM STANDBY FREQUENCY:1", "MHz", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                sim.AddToDataDefinition(DEFINITIONS.RadioState, "COM ACTIVE FREQUENCY:2", "MHz", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                sim.AddToDataDefinition(DEFINITIONS.RadioState, "COM STANDBY FREQUENCY:2", "MHz", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                sim.AddToDataDefinition(DEFINITIONS.RadioState, "TRANSPONDER CODE:1", "bco16", SIMCONNECT_DATATYPE.INT32, 0, SimConnect.SIMCONNECT_UNUSED);
                sim.RegisterDataDefineStruct<RadioState>(DEFINITIONS.RadioState);
                sim.RequestDataOnSimObject(REQUESTS.RadioState, DEFINITIONS.RadioState, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);

                connectButton.Text = "Disconnect";
                SetControlsEnabled(true);
                SetStatus("Connecting...");
            }
            catch (COMException ex)
            {
                sim = null;
                SetStatus("Could not connect (is Prepar3D running?): " + ex.Message);
            }
        }

        private void Disconnect()
        {
            if (InvokeRequired) { BeginInvoke((Action)Disconnect); return; }
            if (sim != null) { sim.Dispose(); sim = null; }
            connectButton.Text = "Connect";
            SetControlsEnabled(false);
            SetStatus("Disconnected");
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke((Action<string>)SetStatus, text); return; }
            statusLabel.Text = text;
        }

        private void OnSimObjectData(SimConnect s, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if ((REQUESTS)data.dwRequestID != REQUESTS.RadioState) return;
            var r = (RadioState)data.dwData[0];
            com1Active.Text = r.Com1Active.ToString("000.000", CultureInfo.InvariantCulture);
            com1Standby.Text = r.Com1Standby.ToString("000.000", CultureInfo.InvariantCulture);
            com2Active.Text = r.Com2Active.ToString("000.000", CultureInfo.InvariantCulture);
            com2Standby.Text = r.Com2Standby.ToString("000.000", CultureInfo.InvariantCulture);
            xpndrCode.Text = r.Transponder.ToString("X4");
        }

        private void SetStandby(EVENTS ev, TextBox input)
        {
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double mhz)
                || mhz < 118.0 || mhz > 136.995)
            {
                SetStatus("Enter a COM frequency between 118.000 and 136.995 MHz");
                return;
            }
            uint bcd = legacyBcd.Checked ? Bcd.FromMHzLegacy16(mhz) : Bcd.FromMHz(mhz);
            Send(ev, bcd);
            SetStatus(string.Format(CultureInfo.InvariantCulture, "Sent {0} = 0x{1:X} ({2:F3} MHz)", ev, bcd, mhz));
        }

        private void SetTransponder()
        {
            string code = xpndrInput.Text.Trim();
            if (code.Length != 4 || code.IndexOfAny(new[] { '8', '9' }) >= 0
                || !uint.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                SetStatus("Squawk must be 4 octal digits (0-7), e.g. 1200");
                return;
            }
            // XPNDR_SET takes the code as BCD16: squawk 1200 -> 0x1200.
            uint bcd = Convert.ToUInt32(code, 16);
            Send(EVENTS.XPNDR_SET, bcd);
            SetStatus("Sent XPNDR_SET = 0x" + bcd.ToString("X4") + " (squawk " + code + ")");
        }

        private void Send(EVENTS ev, uint data)
        {
            if (sim == null) return;
            try
            {
                sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, ev, data, GROUPS.RADIO,
                    SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
            }
            catch (COMException ex)
            {
                SetStatus("Send failed: " + ex.Message);
            }
        }

        protected override void DefWndProc(ref Message m)
        {
            if (m.Msg == WM_USER_SIMCONNECT && sim != null)
            {
                try { sim.ReceiveMessage(); }
                catch (Exception ex) { SetStatus("ReceiveMessage: " + ex.Message); }
            }
            else
            {
                base.DefWndProc(ref m);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            sim?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
