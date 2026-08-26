# CapsCaret

A small Windows utility that shows the Caps Lock state next to your text caret.

CapsCaret was inspired by the small quality-of-life details found in macOS. Instead of checking the keyboard LED or typing a character to find out whether Caps Lock is enabled, the indicator appears directly where you're typing.

## Features

* Shows a Caps Lock indicator next to the active text caret
* Automatically follows the caret while you move through text
* Uses the current Windows accent color
* Runs quietly in the system tray
* Can start automatically with Windows
* Does not steal focus or block mouse clicks
* Supports several Windows accessibility technologies for better application compatibility

## Tested applications

CapsCaret is currently tested and working in:

* Command Prompt (cmd.exe)
* ChatGPT
* Windows Notepad
* Firefox
* Chromium-based browsers
* Discord
* Telegram Desktop

Compatibility with other applications may vary depending on how they expose their text caret to Windows.

## Known issues

* The Windows Start menu search field is currently unsupported.
* In Telegram Desktop, the indicator may not appear while the message field is completely empty. It appears after the first character is entered.

## How it works

Different Windows applications expose their text caret in different ways.

CapsCaret uses several APIs depending on the application:

* Microsoft Active Accessibility
* Windows UI Automation
* Java Access Bridge

Classic Win32 caret detection is intentionally not used as a general fallback because some modern applications expose stale or incorrect native caret coordinates.

## Privacy

CapsCaret runs locally on your computer.

It uses global keyboard and mouse hooks to detect input activity and the Caps Lock state.

CapsCaret does not record, reconstruct, transmit, or store the text you type.

No account or internet connection is required.

## Installation

1. Download the latest release from the **Releases** section of this repository.
2. Extract the archive.
3. Run `CapsCaret.exe`.
4. The application will appear in the Windows system tray.

From the tray menu you can:

* Enable or disable CapsCaret
* Enable **Start with Windows**
* Exit the application

No installer is currently required.

## Beta

CapsCaret is currently in beta.

If you find a problem, please include:

* Windows version
* Display scaling, such as 100%, 125% or 150%
* Number of monitors
* Application where the problem occurred
* Exact steps needed to reproduce the problem

Screenshots or short screen recordings are especially useful for caret positioning bugs.

## Building from source

Clone the repository and build the project in Release configuration:

```bash
dotnet build -c Release
```

CapsCaret is Windows-specific and depends on Windows accessibility and input APIs.

## Security

Official builds should only be downloaded from the GitHub Releases section of this repository.

CapsCaret is open source, so the source code can be inspected directly.

Code signing is planned for public releases. Until the application has established signing and SmartScreen reputation, Windows may display an unfamiliar-app warning for new builds.

If an antivirus reports CapsCaret as suspicious, please open an issue and include:

* Antivirus product
* Detection name
* CapsCaret version

## Contributing

Bug reports, compatibility reports and pull requests are welcome.

Reports about applications that do or do not work with CapsCaret are especially useful.

## Code signing policy

Free code signing is provided by SignPath.io, with a certificate issued by the SignPath Foundation.

### Team roles

- Committer and reviewer: project maintainer
- Approver: project maintainer

### Privacy

CapsCaret does not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

## License

CapsCaret is released under the MIT License.

See `LICENSE` for details.
