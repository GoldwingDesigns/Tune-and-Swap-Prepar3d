# Tune & Swap – Prepar3D v5 SimConnect PoC

Small WinForms app that tunes COM1/COM2 standby frequencies and swaps active/standby
via SimConnect, and shows the live frequencies read back from the sim.

| Action | SimConnect event | Data |
|---|---|---|
| Set COM1 standby | `COM_STBY_RADIO_SET` | BCD Hz |
| Set COM2 standby | `COM2_STBY_RADIO_SET` | BCD Hz |
| Swap COM1 | `COM_STBY_RADIO_SWAP` | – |
| Swap COM2 | `COM2_RADIO_SWAP` | – |

## Build (Windows)

Requires the Prepar3D v5 SDK (for `LockheedMartin.Prepar3D.SimConnect.dll`) and the .NET SDK.

    dotnet build src -c Release -p:P3DSdkPath="C:\Program Files\Lockheed Martin\Prepar3D v5 SDK 5.4.16.30000"

The app must be x64 to talk to the 64-bit P3D v5. Start P3D, load a flight, run `TuneAndSwap.exe`, click **Connect**.

## BCD encoding

Frequency in Hz with the leading "1" dropped, one digit per nibble: 118.275 MHz → `0x18275000`.
If your build ignores this, tick "legacy 16-bit BCD" (118.275 → `0x1827`, 10 kHz steps).
Note: this was written without a P3D install to test against; the BCD format is the thing most worth verifying first.
