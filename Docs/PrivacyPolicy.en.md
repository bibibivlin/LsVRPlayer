# L's VRPlayer Privacy Policy

Effective date: October 17, 2026. Version: 2026-10-17.1.

## Scope and choices

L's VRPlayer plays local, SMB and Emby media. It does not supply movies, operate a public media library or provide a developer account service. On first use, and after a material policy revision requiring renewed confirmation, the app displays a notice before initializing playback and server features. You may agree to continue or decline and exit. The policy, permissions and license pages work offline without media access.

## Local playback

When you select media, folders or subtitles, the app reads their contents, names, paths, sizes and modification times. Viewing history, progress, favorites, per-film projection/subtitle settings and preferences remain on the device. Optional format recognition processes filenames, metadata and small sampled video frames locally. The app does not upload media, subtitles or sampled frames to the developer.

## Servers you choose

LAN discovery and probing run only when you enter the SMB source and start discovery. Connecting to a share sends the necessary authentication information to that server. Server addresses, shares, usernames, domains and preferences are stored locally. SMB passwords are persisted with Android Keystore-backed encryption only when you choose to remember them; otherwise they remain in the current session.

Emby login sends your username, password and client information to the server you specify. Client information includes the app/version, device model and a device identifier used to distinguish playback devices. Emby passwords are never saved. Returned access tokens are encrypted and bound to the server/account when you choose to remember login. The app requests library data, images, subtitles and original videos, and reports playback start, pause, progress, stop and your changes to watched/favorite status. Account metadata and browsing preferences remain local. The server and its administrator may retain information under their own policies.

Traffic flows directly between your device and your configured servers, which may be in another country. You choose whether to connect. The developer receives none of these accounts, tokens, media or viewing records. HTTP is not transport-encrypted. SMB encryption depends on your configuration and the server. Server operators are responsible for their own privacy practices.

## Permissions and platform services

The app explains and requests media access when needed. Denial does not block the legal pages or network sources. The system folder picker grants read access only to the folder you select. Internet, network/Wi-Fi state and multicast support server connections/discovery; wake lock supports foreground viewing. The app does not request recording, camera, location, contacts, SMS, system-setting modification or all-files-management permissions. See the offline permissions page for details and refusal effects.

Unity and PICO provide headset/controller tracking, rendering, audio and lifecycle services. The app uses poses for current-frame interaction, without creating motion histories or sending them to the developer. The OS, PICO Store and platform services have their own policies. The app includes no advertising SDK, developer analytics upload or cross-app tracking service.

## Retention and protection

The release app stores limited performance and network statistics locally, excluding media paths, subtitle text and authentication secrets. Development builds may additionally write local paths, subtitles and UI diagnostics; these are not automatically uploaded. If you email diagnostics, remove private information first. Support messages are used to respond to your request and retained only as necessary, subject to applicable requirements and deletion requests.

Remembered secrets use Android Keystore-backed encryption, separately from ordinary preferences. Android automatic backup is disabled. The app does not sync its data to a developer cloud. Local records remain until you clear them, delete their configuration or uninstall. System folder grants are managed by Android. Removing local data does not remove server records or copies made by you or the operating system outside this app's controls.

## Your controls and rights

Clear viewing history in Settings, remove individual favorites, delete SMB servers, and sign out of or remove Emby accounts. Revoke media permissions, clear app data or uninstall through system settings. Revoke folder grants through the system or clear app data. Manage server-side history using that server's controls or contact its operator. Use the privacy email at the end of this policy to request access, correction, deletion, withdrawal of consent or other rights available where you live. We will verify only necessary details and respond within applicable time limits. Do not email passwords or access tokens. This policy does not limit statutory rights.

## Children and changes

The app is not directed to children and does not create child profiles. Minors should use authorized media with guardian guidance. Guardians may use the privacy email below about a child's information. Updates show a new date; material changes to purposes, data scope or sharing prompt renewed confirmation in the app.

## Developer, project and privacy contact

Developer: [bibibivlin](https://github.com/bibibivlin)

Project: https://github.com/bibibivlin/LsVRPlayer

Privacy contact email: linyoc@outlook.com
