# Browser Dashboard

[⌂ Back to English guide](en-Home) · [Previous: AI Models](en-AI) · [Next: In-Game Overlay](en-Overlay)

> Read chat in your browser and personalize message display.

---

![Chat Dashboard](images/01-dashboard.jpg)

## Open the Dashboard

Right-click the app's system tray icon and select **Dashboard**. It opens in your default browser. Use the tray menu whenever you want to open it rather than relying on an old bookmark, because its browser address may change.

To launch the browser automatically when the app starts, enable that option under **Runtime Settings**.

## Adjust how messages appear

| Control | What it changes |
| --- | --- |
| **Message Display** | Translation only, original only, or both together |
| **Show Source Language** | Whether detected source-language labels are shown |
| **Night Mode** | Switches between lighter and darker viewing styles |
| **Chat Bubble Background** | Shows or hides the background behind messages |
| **Game Status** | Indicates whether the app detects a running game |

Friendly, enemy, and system messages can have different text colors. See [Appearance and Chat Styles](en-Appearance) for adjustments.

## Open the Dashboard from another device

To open the Dashboard from a phone, tablet, or another computer on the same LAN, first check **Runtime Settings → LAN Access** and the Windows Firewall status. The firewall rule is only needed for cross-device access; it is not required when the Dashboard is used only in the local browser.

After enabling LAN access, connect using this PC's **LAN IPv4 address + the Dashboard's current port**. Do not use `127.0.0.1` or `0.0.0.0` from another device.

Runtime Settings automatically prefers the **active interface that currently has traffic and external network access** and generates a QR code for it. A phone or tablet on the same LAN can scan the code to open the Dashboard.

LAN authentication is optional and off by default. When enabled, other devices need a random session token generated for the current app run. **Access from this PC is always exempt**, including when the local LAN address is used instead of localhost.

See [Runtime & Network](en-Preferences) for firewall status, Microsoft Store update behavior, authentication, and rule removal.

## The page is empty or waiting

The game may not be running, no new chat messages may have arrived, or translation may still be in progress. Confirm there is actual new in-game chat before troubleshooting [Translation Settings](en-Translation).

## Why do settings look different in another browser?

Some Dashboard preferences are remembered separately by each browser. If you switch browsers or clear browsing data, you may need to choose them again. Dashboard display preferences can also differ from those in the [Overlay](en-Overlay).

> 💡 Prefer reading translations inside the game? Try the [In-Game Overlay](en-Overlay).

---

[← Previous: AI Models](en-AI) · [Back to English guide](en-Home) · [Next: In-Game Overlay →](en-Overlay)
