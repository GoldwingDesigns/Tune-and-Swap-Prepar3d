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
            COM1_TX_SELECT,
            COM2_TX_SELECT,
            RX_ALL_SET,
            RX_ALL_TOGGLE,
            PILOT_TX_SET,
            COM1_RX_SELECT,
            COM2_RX_SELECT,
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
            public int Com1Transmit; // bool
            public int Com2Transmit; // bool
            public int ReceiveAll;   // bool
        }

        private SimConnect sim;

        private readonly Button connectButton = new Button { Text = "Connect", Width = 90 };
        private readonly Label statusLabel = new Label { Text = "Disconnected", AutoSize = true };
        private readonly Label com1Active = NewReadout();
        private readonly Label com1Standby = NewReadout();
        private readonly Label com2Active = NewReadout();
        private readonly Label com2Standby = NewReadout();
        private readonly TextBox com1Input = new TextBox { Text = "118.270", Width = 80 };
        private readonly TextBox com2Input = new TextBox { Text = "121.900", Width = 80 };
        private readonly Button com1Set = new Button { Text = "Set standby" };
        private readonly Button com2Set = new Button { Text = "Set standby" };
        // AutoCheck is off: the sim is the source of truth, so the boxes only change when the readback says so.
        private readonly RadioButton com1Tx = new RadioButton { Text = "COM1", AutoCheck = false, AutoSize = true };
        private readonly RadioButton com2Tx = new RadioButton { Text = "COM2", AutoCheck = false, AutoSize = true };
        private readonly CheckBox com1Mon = new CheckBox { Text = "COM1", AutoCheck = false, AutoSize = true, Enabled = false };
        private readonly CheckBox com2Mon = new CheckBox { Text = "COM2", AutoCheck = false, AutoSize = true, Enabled = false };
        private readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 90 };
        private readonly Timer verifyTimer = new Timer { Interval = 500 };
        private Action verifyAction;
        private bool com1IsTx, com2IsTx, receiveAll;
        private readonly Label xpndrCode = NewReadout();
        private readonly TextBox xpndrInput = new TextBox { Text = "1200", Width = 80, MaxLength = 4 };
        private readonly Button xpndrSet = new Button { Text = "Set squawk" };
        private readonly Button com1Swap = new Button { Text = "Swap" };
        private readonly Button com2Swap = new Button { Text = "Swap" };

        public MainForm()
        {
            Text = "Prepar3D v5 - COM Tune & Swap PoC";
            ClientSize = new System.Drawing.Size(520, 390);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 5,
                Padding = new Padding(10),
            };
            Controls.Add(grid);

            grid.Controls.Add(connectButton, 0, 0);
            grid.Controls.Add(statusLabel, 1, 0);
            grid.SetColumnSpan(statusLabel, 5);

            AddHeader(grid, "Radio", "Active", "Standby", "New standby (MHz)", "", "", 1);
            AddRow(grid, 2, "COM 1", com1Active, com1Standby, com1Input, com1Set, com1Swap);
            AddRow(grid, 3, "COM 2", com2Active, com2Standby, com2Input, com2Set, com2Swap);

            var audio = new GroupBox { Text = "Audio panel", Dock = DockStyle.Bottom, Height = 90 };
            var txFlow = new FlowLayoutPanel { Left = 12, Top = 20, Width = 220, Height = 60 };
            txFlow.Controls.Add(new Label { Text = "Transmit (one):", AutoSize = true, Width = 100 });
            txFlow.SetFlowBreak(txFlow.Controls[0], true);
            txFlow.Controls.Add(com1Tx);
            txFlow.Controls.Add(com2Tx);
            var monFlow = new FlowLayoutPanel { Left = 250, Top = 20, Width = 240, Height = 60 };
            monFlow.Controls.Add(new Label { Text = "Monitor (any):", AutoSize = true, Width = 100 });
            monFlow.SetFlowBreak(monFlow.Controls[0], true);
            monFlow.Controls.Add(com1Mon);
            monFlow.Controls.Add(com2Mon);
            audio.Controls.Add(txFlow);
            audio.Controls.Add(monFlow);
            Controls.Add(audio);
            Controls.Add(log);
            verifyTimer.Tick += (s, e) => { verifyTimer.Stop(); var a = verifyAction; verifyAction = null; a?.Invoke(); };

            grid.Controls.Add(new Label { Text = "XPDR", AutoSize = true }, 0, 4);
            grid.Controls.Add(xpndrCode, 1, 4);
            grid.Controls.Add(xpndrInput, 3, 4);
            grid.Controls.Add(xpndrSet, 4, 4);


            connectButton.Click += (s, e) => { if (sim == null) Connect(); else Disconnect(); };
            com1Set.Click += (s, e) => SetStandby(EVENTS.COM1_STBY_SET, com1Input);
            com2Set.Click += (s, e) => SetStandby(EVENTS.COM2_STBY_SET, com2Input);
            com1Tx.Click += (s, e) => SelectTransmit(0);
            com2Tx.Click += (s, e) => SelectTransmit(1);
            // The transmit radio always receives, so only the other radio's box is clickable.
            // In the sim, "monitor the other radio" is COM_RECEIVE_ALL.
            com1Mon.Click += (s, e) => SetMonitorOther(1);
            com2Mon.Click += (s, e) => SetMonitorOther(2);
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
            com1Tx.Enabled = com2Tx.Enabled = xpndrSet.Enabled = com1Set.Enabled = com2Set.Enabled = com1Swap.Enabled = com2Swap.Enabled = on;
            if (!on) com1Mon.Enabled = com2Mon.Enabled = false;
        }

        private void Connect()
        {
            try
            {
                sim = new SimConnect("Tune and Swap PoC", Handle, WM_USER_SIMCONNECT, null, 0);

                sim.OnRecvOpen += (s, d) => SetStatus("Connected to " + d.szApplicationName);
                sim.OnRecvQuit += (s, d) => Disconnect();
                sim.OnRecvException += (s, d) => Log("SimConnect exception: " + (SIMCONNECT_EXCEPTION)d.dwException + " (packet " + d.dwSendID + ")");
                sim.OnRecvSimobjectData += OnSimObjectData;

                // Client events -> sim events.
                sim.MapClientEventToSimEvent(EVENTS.COM1_STBY_SET, "COM_STBY_RADIO_SET");
                sim.MapClientEventToSimEvent(EVENTS.COM2_STBY_SET, "COM2_STBY_RADIO_SET");
                sim.MapClientEventToSimEvent(EVENTS.XPNDR_SET, "XPNDR_SET");
                sim.MapClientEventToSimEvent(EVENTS.COM1_TX_SELECT, "COM1_TRANSMIT_SELECT");
                sim.MapClientEventToSimEvent(EVENTS.COM2_TX_SELECT, "COM2_TRANSMIT_SELECT");
                sim.MapClientEventToSimEvent(EVENTS.RX_ALL_SET, "COM_RECEIVE_ALL_SET");
                sim.MapClientEventToSimEvent(EVENTS.RX_ALL_TOGGLE, "COM_RECEIVE_ALL_TOGGLE");
                sim.MapClientEventToSimEvent(EVENTS.PILOT_TX_SET, "PILOT_TRANSMITTER_SET");
                sim.MapClientEventToSimEvent(EVENTS.COM1_RX_SELECT, "COM1_RECEIVE_SELECT");
                sim.MapClientEventToSimEvent(EVENTS.COM2_RX_SELECT, "COM2_RECEIVE_SELECT");
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
                sim.AddToDataDefinition(DEFINITIONS.RadioState, "COM TRANSMIT:1", "bool", SIMCONNECT_DATATYPE.INT32, 0, SimConnect.SIMCONNECT_UNUSED);
                sim.AddToDataDefinition(DEFINITIONS.RadioState, "COM TRANSMIT:2", "bool", SIMCONNECT_DATATYPE.INT32, 0, SimConnect.SIMCONNECT_UNUSED);
                sim.AddToDataDefinition(DEFINITIONS.RadioState, "COM RECEIVE ALL", "bool", SIMCONNECT_DATATYPE.INT32, 0, SimConnect.SIMCONNECT_UNUSED);
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

            if ((r.Com1Transmit != 0) != com1IsTx || (r.Com2Transmit != 0) != com2IsTx || (r.ReceiveAll != 0) != receiveAll)
                Log("Sim state: TX1=" + r.Com1Transmit + " TX2=" + r.Com2Transmit + " RX_ALL=" + r.ReceiveAll);
            com1IsTx = r.Com1Transmit != 0;
            com2IsTx = r.Com2Transmit != 0;
            receiveAll = r.ReceiveAll != 0;
            com1Tx.Checked = com1IsTx;
            com2Tx.Checked = com2IsTx;
            // The transmit radio is always monitored; the other one is monitored when "receive all" is on.
            com1Mon.Checked = com1IsTx || receiveAll;
            com2Mon.Checked = com2IsTx || receiveAll;
            com1Mon.Enabled = !com1IsTx;
            com2Mon.Enabled = !com2IsTx;
        }

        private void SetStandby(EVENTS ev, TextBox input)
        {
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double mhz)
                || mhz < 118.0 || mhz > 136.995)
            {
                SetStatus("Enter a COM frequency between 118.000 and 136.995 MHz");
                return;
            }
            uint bcd = Bcd.FromMHz(mhz);
            Send(ev, bcd);
            SetStatus(string.Format(CultureInfo.InvariantCulture, "Sent {0} = 0x{1:X} ({2:F3} MHz)", ev, bcd, mhz));
        }

        private void Log(string text)
        {
            if (InvokeRequired) { BeginInvoke((Action<string>)Log, text); return; }
            log.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + "\r\n");
        }

        /// <summary>
        /// Makes COM1 (index 0) or COM2 (index 1) the transmit radio. Sends PILOT_TRANSMITTER_SET first and the
        /// COMn_TRANSMIT_SELECT event last, so the explicit select wins if the two disagree on numbering.
        /// </summary>
        private void SelectTransmit(uint index)
        {
            Log("TX -> COM" + (index + 1));
            Send(EVENTS.PILOT_TX_SET, index);
            Send(index == 0 ? EVENTS.COM1_TX_SELECT : EVENTS.COM2_TX_SELECT, 0);
        }

        /// <summary>
        /// Toggles monitoring of the non-transmit radio. Sends COM_RECEIVE_ALL_SET and the per-radio
        /// COMn_RECEIVE_SELECT; if the readback hasn't changed shortly after, falls back to COM_RECEIVE_ALL_TOGGLE.
        /// </summary>
        private void SetMonitorOther(int radio)
        {
            bool want = !receiveAll;
            Log("Monitor COM" + radio + " -> " + (want ? "on" : "off"));
            Send(EVENTS.RX_ALL_SET, want ? 1u : 0u);
            Send(radio == 1 ? EVENTS.COM1_RX_SELECT : EVENTS.COM2_RX_SELECT, want ? 1u : 0u);
            verifyAction = () =>
            {
                if (receiveAll == want) return;
                Log("No change after SET; trying COM_RECEIVE_ALL_TOGGLE");
                Send(EVENTS.RX_ALL_TOGGLE, 0);
            };
            verifyTimer.Stop();
            verifyTimer.Start();
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
