# Mechanics sheet — QuickFind (a file-name search tool in the style of the original)

## Window layout (from the QuickFind project: MainWindow.xaml, Themes/Controls.xaml, Dark.xaml, Light.xaml)
Custom 44 px title bar (logo, menus File/Edit/View/Search/Bookmarks/History/Tools/Help, theme, min/max/close); 236 px sidebar
(Locations with item counts, Saved searches, Index settings); main column margins 8/16/20/16: tabs row + Inspector toggle,
search card (radius 16, 60 px input row, Aa/Word/Path/é/.* group, Size and Modified chips), file-type pills + density toggle,
results card (radius 16, header 42, rows 48 comfortable / 34 compact, radius-8 selection band), inspector card 340 px,
34 px status bar. Colours, five accents (Blue, Slate, Violet, Orange, Teal), fonts (Instrument Sans, JetBrains Mono, Segoe MDL2).
Status texts, empty-state titles/messages, size format (B / KB 2-1-0 decimals) and date format dd/MM/yyyy HH:mm follow MainViewModel.cs.
Light theme, no inspector and comfortable rows are the defaults (as in a first start of the original).

## Earlier analysis of the original program (Everything.exe)

Values found in the original program's code. Addresses are in the original executable.
"estimated" means it was looked for in the code and not found (or not settled).

## Window
| Item | Value | Source |
|---|---|---|
| Menus | File, Edit, View, Search, Bookmarks, Tools, Help | strings 0x1401cfe04..0x1401cfe28; capture ev_3dd9c486 |
| Window size | about 1000 × 720 inside the frame | capture ev_3dd9c486 (1009 × 761 with frame) |
| Filter box | hidden by default (View → Filters shows it) | capture ev_3dd9c486 |
| Sort marker | small caret above the sorted column's header; sorted column tinted light grey | capture ev_3dd9c486 |
| Command line | -search/-s, -filter, -bookmark, -case/-nocase, -diacritics, -matchpath, -ww, -regex | help text 0x1401c66a0 |
| Search box hint | "Search..." | string 0x1401c0aa0 ("Search Everything...", brand removed) |
| Window title | `<search> - <app name>` | format "%s - %s" in 0x140129380 |
| Default columns | Name, Path, Size, Date Modified | column table 0x140202a38..0x140202a50 |
| Optional columns | Date Created, Attributes, Extension, Type | strings 0x1401b7f68, 0x1401b7f98, 0x1401ce5b0 |
| Status bar | "%s objects", "%s objects selected", "1 of 1 objects selected" | 0x1401cfdb8, 0x1401cfdd8, 0x1401cdcc8 |
| Status while indexing | "Scanning %s..." / "Indexing %s..." | 0x1401ca238, 0x1401c3668 |
| Status of match options | CASE, PATH, REGEX, DIACRITICS shown in status bar when on | 0x1401cdd1c, 0x1401cdd04, 0x1401cdcfc, 0x1401ccd28 |
| Column widths | original: Name ≈440, Path ≈505 px, Size and Date scrolled off to the right. This rebuild: Name 340, Path 380, Size 90, Date fills the rest, so all four fit | capture ev_3dd9c486; rebuild widths are a choice |
| Size display | files: KB rounded up with thousands separators; folders blank | estimated (setting text 0x1401cd6f0) |
| Date display | local time, dd/mm/yyyy hh:mm | estimated |

## Search menu options (toggles)
Match Case, Match Whole Words, Match Path, Match Diacritics, Enable Regex — strings 0x1401c23a0, 0x1401c2388, 0x1401c2340; serialised as CASE / WHOLE_WORD / PATH / DIACRITICS / REGEX (0x1400b5c60).

## Filters (Search menu / filter box), sub_1400c15c0
| Filter | Search |
|---|---|
| Everything | (none) |
| Audio | ext:aac;ac3;aif;aifc;aiff;au;cda;dts;fla;flac;it;m1a;m2a;m3u;m4a;m4b;m4p;mid;midi;mka;mod;mp2;mp3;mpa;ogg;ra;rmi;snd;spc;umx;voc;wav;wma;xm |
| Compressed | ext:7z;ace;arj;bz2;cab;gz;gzip;jar;r00..r29;rar;tar;tgz;z;zip |
| Document | ext:c;chm;cpp;csv;cxx;doc;docm;docx;dot;dotm;dotx;h;hpp;htm;html;hxx;ini;java;lua;mht;mhtml;odt;pdf;potx;potm;ppam;ppsm;ppsx;pps;ppt;pptm;pptx;rtf;sldm;sldx;thmx;txt;vsd;wpd;wps;wri;xlam;xls;xlsb;xlsm;xlsx;xltm;xltx;xml |
| Executable | ext:bat;cmd;exe;msi;msp;scr |
| Folder | folder: |
| Picture | ext:ani;bmp;gif;ico;jpe;jpeg;jpg;pcx;png;psd;tga;tif;tiff;webp;wmf |
| Video | ext:3g2;3gp;...;wmv (full list at 0x1401bd540) |
Macros audio:, zip:, doc:, exe:, pic:, video: use the same lists (help text 0x1401c81e0).

## Search syntax (help text 0x1401c81e0, parser sub_140116168 / sub_140114300)
- space = AND, `|` = OR, `!` = NOT, `< >` = grouping, `"..."` = exact phrase.
- `*` zero or more characters, `?` one character; a wildcard term matches the whole file name.
- Modifiers: case:, diacritics:, file:, folder:, path:, regex:, wholeword:/ww:, wfn:/wholefilename:, startwith:, endwith:; `no` prefix disables (nocase:, nopath:, nowholeword:, noregex:).
- A term containing `\` is matched against the full path (estimated rule, from the "path:" behaviour).
- Functions implemented: ext:a;b, size:, dm:/datemodified:, dc:/datecreated:, infolder:, parent:, len:, count:, dupe:, empty:, attrib:.
- Not implemented (need content or media metadata): content:, album:, artist:, dimensions:, etc.

## size: (sub_140055200, thresholds at 0x1401fed10)
| Keyword | Range (bytes) |
|---|---|
| empty | 0 |
| tiny | 1 – 10,240 |
| small | 10,241 – 102,400 |
| medium | 102,401 – 1,048,576 |
| large | 1,048,577 – 16,777,216 |
| huge | 16,777,217 – 134,217,728 |
| gigantic | > 134,217,728 |
| unknown | size not known |
Units: kb=1024, mb=1024², gb=1024³, tb=1024⁴, pb, eb. A number with a unit matches a 1 KB-wide band ending at the value rounded up to 1024 (lo = hi − 1023). `a..b` or `a-b` are ranges; `<`, `>`, `<=`, `>=`, `=` comparisons.

## dates (sub_14005eeb0, strings 0x1401ba378..0x1401ba4dc)
today, yesterday, tomorrow; this/last/past/next + week/month/year; lastNdays / pastNweeks / lastNmonths / lastNyears / lastNhours / lastNminutes; explicit yyyy, yyyy-mm, yyyy-mm-dd or d/m/yyyy; comparisons and `..` ranges.

## Export
File list export header "Filename,Size,Date Modified,Date Created,Attributes" with sizes and FILETIME dates as integers and attributes as a number (0x1400f6f20). CSV of the visible results.

## Indexing
The original reads the NTFS master file table and USN journal. This rebuild walks every fixed drive with parallel directory reads, saves the index to disk, loads it at the next start and refreshes it in the background. Live updates use the folder change watcher. Reparse points (junctions/links) are listed but not followed.
