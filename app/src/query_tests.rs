//! Search tests: tokens, sizes and whole searches over a small sample index.

use super::*;

#[test]
fn tokens() {
    assert_eq!(tokenize("a b|c"), vec![Tok::Word("a".into()), Tok::Word("b".into()), Tok::Or, Tok::Word("c".into())]);
    assert_eq!(tokenize("size:>1mb"), vec![Tok::Word("size:>1mb".into())]);
    assert_eq!(tokenize("!<a|b>"), vec![Tok::Not, Tok::Open, Tok::Word("a".into()), Tok::Or, Tok::Word("b".into()), Tok::Close]);
    assert_eq!(tokenize("\"my file\""), vec![Tok::Word("my file".into())]);
}
fn sample() -> Index {
    use crate::index::{NONE, NO_SIZE};
    let mut idx = Index::default();
    idx.set_dir(0, "C:".into());
    idx.set_dir(1, "C:\\Photos".into());
    let rows = [
        ("C:", NONE, 0, NO_SIZE),
        ("Photos", 0, 1, NO_SIZE),
        ("Holiday Beach.JPG", 1, NONE, 3 * 1024 * 1024),
        ("notes.txt", 0, NONE, 500),
        ("Café menu.pdf", 0, NONE, 20_000),
        ("beachball.png", 1, NONE, 0),
    ];
    for (name, dir, self_dir, size) in rows {
        let attrs = if self_dir != NONE { ATTR_DIRECTORY } else { 0 };
        let e = Entry::new(&mut idx.names, name, dir, self_dir, size, 0, 0, attrs);
        idx.entries.push(e);
    }
    idx.finalize();
    idx
}
fn names(q: &str, opts: Options) -> Vec<String> {
    let idx = sample();
    let q = compile(q, &opts).unwrap();
    idx.entries.iter().filter(|e| q.matches(&idx, e)).map(|e| idx.name(e).to_string()).collect()
}
#[test]
fn searches() {
    let o = Options::default();
    assert_eq!(names("beach", o), vec!["Holiday Beach.JPG", "beachball.png"]);
    assert_eq!(names("beach !png", o), vec!["Holiday Beach.JPG"]);
    assert_eq!(names("notes | cafe", o), vec!["notes.txt", "Café menu.pdf"]);
    assert_eq!(names("ext:jpg;txt", o), vec!["Holiday Beach.JPG", "notes.txt"]);
    assert_eq!(names("*.png", o), vec!["beachball.png"]);
    assert_eq!(names("\"caf? *\"", o), vec!["Café menu.pdf"]);
    assert_eq!(names("*beach*", o), vec!["Holiday Beach.JPG", "beachball.png"]);
    assert_eq!(names("beach*.?pg", o), Vec::<String>::new());
    assert_eq!(names("holiday*.?pg", o), vec!["Holiday Beach.JPG"]);
    assert_eq!(names("size:large", o), vec!["Holiday Beach.JPG"]);
    assert_eq!(names("size:empty", o), vec!["beachball.png"]);
    assert_eq!(names("folder:", o), vec!["C:", "Photos"]);
    assert_eq!(names("photos\\ beach", o), vec!["Holiday Beach.JPG", "beachball.png"]);
    assert_eq!(names("pic: holiday", o), vec!["Holiday Beach.JPG"]);
    assert_eq!(names("case:Beach", o), vec!["Holiday Beach.JPG"]);
    assert_eq!(names("ww:beach", o), vec!["Holiday Beach.JPG"]);
    assert_eq!(names("<notes|menu> ext:pdf", o), vec!["Café menu.pdf"]);
    assert_eq!(names("infolder:c:\\photos", o).len(), 2);
    assert_eq!(names("empty:", o), Vec::<String>::new());
    assert_eq!(names("regex:^b.*g$", o), vec!["beachball.png"]);
    let d = Options { diacritics: true, ..o };
    assert_eq!(names("cafe", d), Vec::<String>::new());
}
#[test]
fn sizes() {
    assert_eq!(size_value("1mb"), Some((1048576 - 1023, 1048576)));
    assert_eq!(size_value("tiny"), Some((1, 10240)));
    assert_eq!(size_value("100"), Some((100, 100)));
}
