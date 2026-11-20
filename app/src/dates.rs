//! Date keywords for dm:/dc: (today, yesterday, thisweek, last2weeks, 2024-05 ...).

use chrono::{Datelike, Duration, Local, NaiveDate, NaiveDateTime, TimeZone};

pub fn filetime_to_unix(ft: u64) -> i64 {
    (ft / 10_000_000) as i64 - 11_644_473_600
}

fn ts(d: NaiveDateTime) -> i64 {
    Local.from_local_datetime(&d).earliest().map(|t| t.timestamp()).unwrap_or_else(|| d.and_utc().timestamp())
}

fn day(d: NaiveDate) -> i64 {
    ts(d.and_hms_opt(0, 0, 0).unwrap())
}

fn add_months(d: NaiveDate, m: i32) -> NaiveDate {
    let total = d.year() * 12 + d.month0() as i32 + m;
    NaiveDate::from_ymd_opt(total.div_euclid(12), total.rem_euclid(12) as u32 + 1, 1).unwrap()
}

/// One value -> [start, end) in unix seconds.
fn value(v: &str) -> Option<(i64, i64)> {
    let v = v.trim().to_ascii_lowercase();
    let now = Local::now().naive_local();
    let today = now.date();
    let week_start = today - Duration::days(today.weekday().num_days_from_monday() as i64);
    let month_start = NaiveDate::from_ymd_opt(today.year(), today.month(), 1)?;
    let year_start = NaiveDate::from_ymd_opt(today.year(), 1, 1)?;
    let period = |unit: &str, offset: i32| -> Option<(i64, i64)> {
        Some(match unit {
            "day" => {
                let d = today + Duration::days(offset as i64);
                (day(d), day(d + Duration::days(1)))
            }
            "week" => {
                let d = week_start + Duration::weeks(offset as i64);
                (day(d), day(d + Duration::weeks(1)))
            }
            "month" => (day(add_months(month_start, offset)), day(add_months(month_start, offset + 1))),
            "year" => (day(add_months(year_start, offset * 12)), day(add_months(year_start, offset * 12 + 12))),
            _ => return None,
        })
    };
    match v.as_str() {
        "today" => return period("day", 0),
        "yesterday" => return period("day", -1),
        "tomorrow" => return period("day", 1),
        _ => {}
    }
    for (prefix, kind) in [("this", 0), ("last", -1), ("past", 2), ("next", 1)] {
        if let Some(rest) = v.strip_prefix(prefix) {
            let digits: String = rest.chars().take_while(|c| c.is_ascii_digit()).collect();
            let unit = rest[digits.len()..].trim_end_matches('s');
            let n: i64 = digits.parse().unwrap_or(1);
            if digits.is_empty() && kind != 2 {
                if let Some(r) = period(unit, kind) {
                    return Some(r);
                }
            }
            // last<n><unit> / past<n><unit>: from n units ago until now.
            let back = match unit {
                "minute" => Duration::minutes(n),
                "hour" => Duration::hours(n),
                "day" => Duration::days(n),
                "week" => Duration::weeks(n),
                "month" => Duration::days(30 * n),
                "year" => Duration::days(365 * n),
                _ => return None,
            };
            let end = ts(now);
            return Some(if kind == 1 { (end, ts(now + back)) } else { (ts(now - back), end + 1) });
        }
    }
    // Explicit dates: yyyy, yyyy-mm, yyyy-mm-dd, d/m/yyyy.
    let parts: Vec<&str> = v.split(|c| c == '-' || c == '/' || c == '.').collect();
    let nums: Vec<i32> = parts.iter().map(|p| p.parse().ok()).collect::<Option<_>>()?;
    match nums.as_slice() {
        [y] if *y > 1600 => {
            let s = NaiveDate::from_ymd_opt(*y, 1, 1)?;
            Some((day(s), day(add_months(s, 12))))
        }
        [y, m] if *y > 1600 => {
            let s = NaiveDate::from_ymd_opt(*y, *m as u32, 1)?;
            Some((day(s), day(add_months(s, 1))))
        }
        [y, m, d] if *y > 1600 => {
            let s = NaiveDate::from_ymd_opt(*y, *m as u32, *d as u32)?;
            Some((day(s), day(s + Duration::days(1))))
        }
        [d, m, y] if *y > 1600 => {
            let s = NaiveDate::from_ymd_opt(*y, *m as u32, *d as u32)?;
            Some((day(s), day(s + Duration::days(1))))
        }
        _ => None,
    }
}

/// A dm:/dc: argument, with optional comparison or `a..b` range.
pub fn parse_range(arg: &str) -> Option<(i64, i64)> {
    let a = arg.trim();
    if let Some(v) = a.strip_prefix("<=") {
        return value(v).map(|(_, e)| (i64::MIN, e));
    }
    if let Some(v) = a.strip_prefix(">=") {
        return value(v).map(|(s, _)| (s, i64::MAX));
    }
    if let Some(v) = a.strip_prefix('<') {
        return value(v).map(|(s, _)| (i64::MIN, s));
    }
    if let Some(v) = a.strip_prefix('>') {
        return value(v).map(|(_, e)| (e, i64::MAX));
    }
    if let Some(v) = a.strip_prefix('=') {
        return value(v);
    }
    if let Some((l, r)) = a.split_once("..") {
        let (s, _) = value(l)?;
        let (_, e) = value(r)?;
        return Some((s, e));
    }
    value(a)
}

/// FILETIME -> "dd/mm/yyyy hh:mm" in local time (estimated format).
pub fn format_filetime(ft: u64) -> String {
    if ft == 0 {
        return String::new();
    }
    match Local.timestamp_opt(filetime_to_unix(ft), 0).single() {
        Some(t) => t.format("%d/%m/%Y %H:%M").to_string(),
        None => String::new(),
    }
}
