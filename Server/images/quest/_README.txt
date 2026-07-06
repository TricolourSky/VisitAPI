Custom quest icons — VisitAPI
=============================

Drop a quest icon image into this folder (images/quest/). Supported: .png .jpg .jpeg .bmp

On server start, VisitApiImageLoader registers one image route per file. Reference the icon in a
quest JSON's "image" field using this URL (note: NO path under SPT_Data is needed — the file stays
inside the mod):

    "image": "/files/quest/icon/<filename-without-extension>.<extension>"

Example: a file named  sora.png  in this folder is referenced as:

    "image": "/files/quest/icon/sora.png"

Notes
-----
- The filename (without extension) becomes the URL key, so keep names unique and avoid clashing with
  vanilla 24-hex quest-icon ids.
- Any image extension works at request time (SPT strips it before matching and serves the real file),
  but keep the JSON extension matching the actual file for clarity.
- Changing icons or adding files requires a server restart (routes register on load).

sora.png here is a seed sample (a copy of a vanilla icon) so the SORA demo shows something — replace
it with your own art (keep the name, or rename and update the quest's image field).
