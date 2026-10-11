# Content Filtering

[⌂ Back to English guide](en-Home) · [Previous: Chat Voice Playback](en-Chat-TTS) · [Next: Runtime & Network](en-Preferences)

> Control inappropriate content independently for on-screen text and voice playback.

![Content filtering settings](images/10-content-filter.png)

---

## Separate display and TTS switches

- **Filter on-screen text** masks matching terms as `****` in the Dashboard and Overlay.
- **Filter voice playback** affects TTS only and does not change the text shown on screen.

This is useful for streaming: a private Dashboard can keep the complete translation while spoken audio sent to the stream is filtered separately.

## Filtering level

The overall level is a minimum severity threshold. It applies to built-in and custom blocked terms:

- **Severe content only** — filters severity 3 entries only.
- **Common offensive content** — filters severity 2 and 3 entries (the default).
- **Strict filtering (broadest)** — also filters severity 1 entries. This catches more terms but can increase false positives.

## Custom blocklist and allowlist

When adding a blocked term, choose its **severity** (1 = Broad, 2 = Normal, 3 = Severe) and **matching mode**:

- **Auto** — typically matches whole words for Latin-script words and substrings for Chinese characters.
- **Whole word** — matches at word boundaries, helping avoid matches inside longer words.
- **Contains** — matches the term anywhere inside the text, including within longer words.

Add a term to the **allowlist** if a game name or common expression would otherwise be incorrectly filtered; the allowlist takes precedence over matching blocked terms. You can also select and remove custom entries. The bundled Chinese and English dictionaries come from LDNOOBW; the source is linked on the About page.

## Importing and exporting custom lists

- **Import TXT**: one blocked term per non-empty line (lines beginning with `#` are ignored). Each is assigned severity **2 (Normal)** and matching mode **Auto**. TXT import does not populate the allowlist.
- **Import JSON**: restores custom blocked terms (including severity and matching mode) and allowlisted terms from an exported filter package.
- **Export JSON**: backs up your **custom** blocklist and allowlist, not the bundled base dictionaries.

**Imports merge with your existing entries; they do not replace the lists.** Duplicate blocked terms with the same effective matching mode keep the higher severity, and duplicate allowlist terms are not added again. To remove an unwanted entry, select and remove it from the corresponding list.

## TTS handling

For a TTS match, choose whether to remove the matched term and continue speaking or skip the entire chat message. Display and TTS behavior remain independent.

---

[← Previous: Chat Voice Playback](en-Chat-TTS) · [Back to English guide](en-Home) · [Next: Runtime & Network →](en-Preferences)
