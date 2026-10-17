# Tune & Swap – Prepar3D v5 SimConnect PoC

A small WinForms app (C#, .NET Framework 4.8, x64) that shows how to **tune and swap COM radios and set the
transponder in Prepar3D v5 using SimConnect**. It also reads the live values back from the sim so you can see
the changes land.

## TL;DR – how the SimConnect events work

You don't write to a radio variable. You **transmit a client event**; the sim performs the same action as the
equivalent cockpit control. A "set" event carries its value in the event's single 32-bit `dwData` argument,
**encoded as BCD (binary-coded decimal)**: every decimal digit of the value occupies one 4-bit nibble, so the
number you *read* in hex is the number you *mean* in decimal.

| What | Sim event name | `dwData` | Example |
|---|---|---|---|
| Set COM1 standby | `COM_STBY_RADIO_SET` | 16-bit BCD, 10 kHz steps, leading "1" dropped | 118.27 MHz → `0x1827` |
| Set COM2 standby | `COM2_STBY_RADIO_SET` | same as above | 121.90 MHz → `0x2190` |
| Swap COM1 active ↔ standby | `COM_STBY_RADIO_SWAP` (alias `COM1_RADIO_SWAP`) | unused (send `0`) | – |
| Swap COM2 active ↔ standby | `COM2_RADIO_SWAP` | unused (send `0`) | – |
| Set transponder code | `XPNDR_SET` | 16-bit BCD, the four squawk digits | squawk 1200 → `0x1200` |

### Step by step

For each event you want to use:

1. **Map** a client-side ID to the sim event name (once, after connecting):

       sim.MapClientEventToSimEvent(EVENTS.COM1_STBY_SET, "COM_STBY_RADIO_SET");

2. **Add it to a notification group** and give the group a priority (this is what lets your event take effect
   rather than being masked):

       sim.AddClientEventToNotificationGroup(GROUPS.RADIO, EVENTS.COM1_STBY_SET, false);
       sim.SetNotificationGroupPriority(GROUPS.RADIO, SimConnect.SIMCONNECT_GROUP_PRIORITY_HIGHEST);

3. **Transmit** it to the user's aircraft with the BCD value as the data argument:

       sim.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, EVENTS.COM1_STBY_SET,
           0x1827, GROUPS.RADIO, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);

   A swap is identical but with data `0`.

4. The new standby frequency appears in the sim; send the swap event to make it active.

### Working out the BCD value

**COM frequency (16-bit BCD).** Take the frequency in MHz, drop the leading `1`, and write the remaining four
digits (MHz units, tens of kHz). Each digit becomes one hex digit:

    118.27 MHz  →  digits 1 8 2 7  (after the dropped "1")  →  0x1827
    121.90 MHz  →  digits 2 1 9 0                           →  0x2190
    136.97 MHz  →  digits 3 6 9 7                           →  0x3697

In code (`src/Bcd.cs`): `round(mhz * 100) % 10000`, then pack each decimal digit into a nibble.
Resolution is 10 kHz, so a 25 kHz channel such as 118.275 is not exactly representable; the sim snaps to
the nearest channel it supports.

**Transponder (16-bit BCD).** Each squawk digit is an octal digit (0–7) written as one hex digit, so the
hex literal *looks like* the squawk: `0x1200` = squawk 1200, `0x7700` = 7700. In C#,
`Convert.ToUInt32("1200", 16)` does exactly this.

> **Gotcha that cost us time:** the SimConnect documentation describes the COM set events as "BCD Hz".
> A 32-bit BCD of the frequency in Hz (118.275 MHz → `0x18275000`) did **not** tune the radio in our
> Prepar3D v5 testing. The 16-bit form above (`0x1827`) did. If your radio doesn't change, check
> the encoding first.

### Reading the values back

The app registers one data definition and requests it whenever it changes:

| SimVar | Units | Type |
|---|---|---|
| `COM ACTIVE FREQUENCY:1` / `:2` | `MHz` | `FLOAT64` |
| `COM STANDBY FREQUENCY:1` / `:2` | `MHz` | `FLOAT64` |
| `TRANSPONDER CODE:1` | `bco16` | `INT32` (shown as hex, so `0x1200` reads as `1200`) |

## Using the app

1. Start Prepar3D v5 and load a flight.
2. Run `TuneAndSwap.exe` and click **Connect**.
3. Type a frequency (118.000–136.995 MHz) or squawk (4 digits, each 0–7) and click **Set standby** / **Set squawk**.
4. Click **Swap** to exchange active and standby.

## Build (Windows)

Requires the Prepar3D v5 SDK (for `LockheedMartin.Prepar3D.SimConnect.dll`) and the .NET SDK.

    dotnet build src -c Release -p:P3DSdkPath="C:\Program Files\Lockheed Martin\Prepar3D v5 SDK 5.4.16.30000"

The app must be **x64** to talk to 64-bit Prepar3D v5. The window message loop (`WM_USER_SIMCONNECT`) drives
`SimConnect.ReceiveMessage()`; see `src/MainForm.cs`.

## Files

- `src/MainForm.cs` – UI, SimConnect setup, event transmit, data readback
- `src/Bcd.cs` – COM frequency → 16-bit BCD
- `src/Program.cs` – entry point
