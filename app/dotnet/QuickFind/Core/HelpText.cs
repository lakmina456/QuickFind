namespace QuickFind.Core;

public static class HelpText
{
    public const string Syntax = """
Operators
  space        AND
  |            OR
  !            NOT
  < >          Grouping
  " "          Exact phrase

Wildcards
  *            Zero or more characters
  ?            One character
  (a wildcard search matches the whole file name)

Modifiers  (add no in front to turn off, e.g. nocase:)
  case:        Match case
  diacritics:  Match accents
  file:        Files only
  folder:      Folders only
  path:        Match the full path
  regex:       Regular expression
  ww:          Whole words only
  wfn:         Whole file name
  startwith:   Names starting with text
  endwith:     Names ending with text

Functions
  ext:jpg;png          Extensions
  size:>10mb           Size (also tiny, small, medium,
                       large, huge, gigantic, a..b)
  dm:today             Date modified (yesterday, thisweek,
                       lastmonth, last3days, 2024-05, a..b)
  dc:2024              Date created
  infolder:C:\Temp     Directly inside a folder
  len:>20              File name length
  attrib:H             Attributes (R H S D A C E)
  empty:               Empty folders
  dupe:                Names that appear more than once
  content:text         Search inside file contents (on demand)
  count:100            Show at most 100 results

Macros
  audio:  zip:  doc:  exe:  pic:  video:

Keys
  Ctrl+I case   Ctrl+B whole words   Ctrl+U path
  Ctrl+M accents   Ctrl+R regex   Ctrl+F search box
  Ctrl+Alt+Space   Show from tray (global)
  Enter opens, Down moves into the list.

Indexing
  Prefer NTFS reads the volume file table (MFT/USN) when
  running as administrator for near-instant catalogs.
""";
}
