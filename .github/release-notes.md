Fixes exports stopping at forwarded messages that mention inaccessible channels.

- Channel mentions that return HTTP 403 now display as `#unavailable-channel`, and the export continues.
- Deleted channels still display as `#deleted-channel`.
- The fix applies to JSON, text, CSV, SQLite, and HTML formatting; raw message content is preserved.
- Windows updates and the app's release links now use Nailington's fork.

Includes self-contained GUI and CLI downloads for Windows, Linux, and macOS. Existing resumable exports, media downloads, SQLite search, and offline conversion features are retained from wefalltomorrow's `2.48.0-wft.3` release.

Based on DiscordChatExporter by Tyrrrz and the maintained fork by wefalltomorrow. See `NOTICE` and `License.txt` for credits and licensing.
