# Display backend

The backend targets the installed display helper at:

```
C:\ProgramData\HP\LCDDisplayHelper\OMENLCDDisplayHelper.exe
```

Configuration root:

```
%LOCALAPPDATA%\Publishers\v10z8vjag6ke6\LCDDisplay
```

`profilelcdsettings.json` selects the active `LCDCurrentModeId`. `globallcdsettings.json` has a Newtonsoft-style `$values` collection of effect records. The app finds the active record and updates:

| Record | Field | Value |
| --- | --- | --- |
| Effect | `LCDEffectMode` | `Image` or `Video` |
| Effect | `LCDEffectFPS` | 1–30 |
| Effect | `CurrentPath.$values` | One absolute prepared-media path |
| Effect | `SlideShowDuration` | `1` |
| Profile | `LCDOnOff` | `true` |
| Profile | `LCDDisplayCurrentGame` | `false` |
| Profile | `LCDDisplayOGHEvent` | `false` |

The helper's native display transport was identified as HP USB VID `03F0`, PID `7397`, interface `01`, with `CWitmodHid_SynchronizeImage(handle, jpegPath)` in `witmodSdkOther.dll`. The app does not call this native API directly. HP's helper owns decoding and USB communication. Its authenticated named-pipe API is also not used.

The composition model is shared between the preview and exporter: scale to fit or fill, apply a 25–300% zoom, then position with ±480 px offsets. The FFmpeg graph pads before cropping so moving media in either direction produces a black background. Still images use the same computed rectangle through GDI+.

Configuration writes use a temporary file and atomic replacement per file. The two-file update is not globally atomic; if an ordinary Apply operation fails, the app rolls both settings back to their pre-Apply values. A process or OS crash between writes still requires recovery from the saved originals. Recovery files remain if restarting the helper fails.

The app validates the helper executable path before terminating a running helper. It does not stop other HP services. FPS is a requested target; HP's implementation adds its own timing overhead. GIF timing is normalized to the chosen FPS by conversion. Video previews use a downscaled first frame, so unusual aspect ratios may differ by a pixel due to rounding.

Current limitations include reliance on HP's private config schema and helper location, potential conflicts with active Hub edits, and testing on a single GT23 system. Switching composition restarts the display helper and can briefly blank the LCD.
