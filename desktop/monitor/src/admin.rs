//! The Admin section's state: the site's `/admin/accounts` and `/admin/reports` from the Mac. Accounts are found by
//! name or sign-in id and opened for their role, granted plan, suspension and recorded changes; reports list open
//! ones first (oldest first, as the API gives them), filtered by status and reason. Every change waits in a
//! [`Confirm`] until it is confirmed with a reason or note, and the API's own words come back when it refuses.
//!
//! The rules are the API's (`AccountAccessService`, `ContentReportService`); the app mirrors them so it never offers
//! what would be refused: an admin can't change their own role or suspend themselves or an Admin, urgent reports
//! (child safety, terrorism) are removed rather than taken down, and others are taken down rather than removed.

use reelshelf_api::{AdminAccount, AdminAccountDetail, AdminContentReport};
use rune::TextEdit;
use uuid::Uuid;

use crate::state::{Fetched, Load};

/// The section's two tabs.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum AdminTab {
    Accounts,
    Reports,
}

/// The report list's status filter: the open reports, or the latest dealt with one way.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum StatusFilter {
    Open,
    TakenDown,
    Dismissed,
    Removed,
}

impl StatusFilter {
    pub const ALL: [StatusFilter; 4] = [
        StatusFilter::Open,
        StatusFilter::TakenDown,
        StatusFilter::Dismissed,
        StatusFilter::Removed,
    ];

    /// The report status it keeps, as the API spells it.
    pub fn status(self) -> &'static str {
        match self {
            StatusFilter::Open => "open",
            StatusFilter::TakenDown => "taken_down",
            StatusFilter::Dismissed => "dismissed",
            StatusFilter::Removed => "removed",
        }
    }

    /// Read from `GET /api/admin/reports?resolved=true` rather than the open list.
    pub fn resolved(self) -> bool {
        self != StatusFilter::Open
    }
}

/// The roles, as the API spells them, in the order they're offered.
pub const ROLES: [&str; 3] = ["viewer", "editor", "admin"];

/// The tiers that can be granted, after "none".
pub const GRANTS: [Option<&str>; 4] = [None, Some("plus"), Some("pro"), Some("unlimited")];

/// Report reasons, as the API spells them, in the order the site's form lists them.
pub const REASONS: [&str; 7] = [
    "child_safety",
    "terrorism",
    "sexual",
    "violence",
    "hate_or_harassment",
    "copyright",
    "other",
];

/// Child-safety and terrorism reports hide the share link at once and are removed, never taken down
/// (`ContentReportReasons.Urgent`).
pub fn is_urgent(reason: &str) -> bool {
    matches!(reason, "child_safety" | "terrorism")
}

/// A change waiting for its confirmation, naming what it changes.
#[derive(Clone, Debug, PartialEq)]
pub enum Change {
    Role {
        account: Uuid,
        name: String,
        from: String,
        to: String,
    },
    GrantedTier {
        account: Uuid,
        name: String,
        from: Option<String>,
        to: Option<String>,
    },
    Suspend {
        account: Uuid,
        name: String,
    },
    LiftSuspension {
        account: Uuid,
        name: String,
    },
    TakeDown {
        report: Uuid,
        clip: String,
    },
    Dismiss {
        report: Uuid,
        clip: String,
    },
    /// Deletes the clip, keeps it as evidence and suspends its owner: it can't be undone.
    Remove {
        report: Uuid,
        clip: String,
        owner: Option<String>,
    },
}

impl Change {
    /// Every change but a dismissal needs words for the record (the API refuses it without).
    pub fn note_required(&self) -> bool {
        !matches!(self, Change::Dismiss { .. })
    }

    /// The account it changes, for an account change.
    pub fn account(&self) -> Option<Uuid> {
        match self {
            Change::Role { account, .. }
            | Change::GrantedTier { account, .. }
            | Change::Suspend { account, .. }
            | Change::LiftSuspension { account, .. } => Some(*account),
            _ => None,
        }
    }

    /// Red: it takes something away from someone.
    pub fn destructive(&self) -> bool {
        matches!(
            self,
            Change::Suspend { .. } | Change::TakeDown { .. } | Change::Remove { .. }
        )
    }
}

/// The confirmation dialog: the change, the reason or note typed for it, and how sending it went.
pub struct Confirm {
    pub change: Change,
    pub note: TextEdit,
    /// Sent and not yet answered.
    pub busy: bool,
    /// The API's words when it refused.
    pub error: Option<String>,
}

impl Confirm {
    pub fn new(change: Change) -> Confirm {
        Confirm {
            change,
            note: TextEdit::new(""),
            busy: false,
            error: None,
        }
    }

    /// The note, trimmed; None when empty.
    pub fn note(&self) -> Option<String> {
        let note = self.note.text.trim();
        (!note.is_empty()).then(|| note.to_owned())
    }

    pub fn can_send(&self) -> bool {
        !self.busy && (self.note().is_some() || !self.change.note_required())
    }
}

/// The Admin section's state.
pub struct Admin {
    pub tab: AdminTab,
    /// Accounts: the search box, and the answer for the query in `searched`.
    pub query: TextEdit,
    pub searched: String,
    pub results: Option<Load<Vec<AdminAccount>>>,
    /// The account open, and its detail.
    pub account: Option<Uuid>,
    pub detail: Option<Load<AdminAccountDetail>>,
    /// Reports: the filters, the open list and the latest dealt with.
    pub status: StatusFilter,
    pub reason: Option<String>,
    pub reason_menu: bool,
    pub open: Fetched<Vec<AdminContentReport>>,
    pub resolved: Fetched<Vec<AdminContentReport>>,
    /// The report open.
    pub report: Option<Uuid>,
    pub confirm: Option<Confirm>,
    /// 403: the account isn't an Admin.
    pub forbidden: bool,
    /// What the last confirmed change did, shown under the account or report it changed.
    pub done: Option<String>,
}

impl Default for Admin {
    fn default() -> Self {
        Admin {
            tab: AdminTab::Reports,
            query: TextEdit::new(""),
            searched: String::new(),
            results: None,
            account: None,
            detail: None,
            status: StatusFilter::Open,
            reason: None,
            reason_menu: false,
            open: Fetched::default(),
            resolved: Fetched::default(),
            report: None,
            confirm: None,
            forbidden: false,
            done: None,
        }
    }
}

impl Admin {
    /// The query to search for, when it is long enough (the API answers nothing under two characters).
    pub fn search_query(&self) -> Option<String> {
        let q = self.query.text.trim();
        (q.chars().count() >= 2).then(|| q.to_owned())
    }

    /// The list the status filter reads.
    pub fn list(&self) -> &Fetched<Vec<AdminContentReport>> {
        if self.status.resolved() {
            &self.resolved
        } else {
            &self.open
        }
    }

    /// The reports the filters keep, in the API's order (open: oldest first; dealt with: latest first).
    pub fn reports(&self) -> Vec<&AdminContentReport> {
        self.list()
            .value
            .iter()
            .flatten()
            .filter(|r| r.status == self.status.status())
            .filter(|r| self.reason.as_deref().is_none_or(|reason| r.reason == reason))
            .collect()
    }

    /// The report open, from either list.
    pub fn selected_report(&self) -> Option<&AdminContentReport> {
        let id = self.report?;
        self.open
            .value
            .iter()
            .chain(self.resolved.value.iter())
            .flatten()
            .find(|r| r.id == id)
    }

    /// Open reports, and how many of them are urgent; None until read.
    pub fn open_counts(&self) -> Option<(usize, usize)> {
        let open = self.open.value.as_ref()?;
        let open: Vec<_> = open.iter().filter(|r| r.status == "open").collect();
        Some((open.len(), open.iter().filter(|r| is_urgent(&r.reason)).count()))
    }

    /// A report as it now is: replaced where it shows, moved from the open list to the top of those dealt with once
    /// it was.
    pub fn report_changed(&mut self, report: AdminContentReport) {
        if let Some(open) = &mut self.open.value {
            open.retain(|r| r.id != report.id);
            if report.status == "open" {
                open.push(report.clone());
            }
        }
        if report.status != "open" {
            let resolved = self.resolved.value.get_or_insert_with(Vec::new);
            resolved.retain(|r| r.id != report.id);
            resolved.insert(0, report);
        }
    }

    /// An account as it now is, in the detail and the search results.
    pub fn account_changed(&mut self, detail: AdminAccountDetail) {
        if let Some(Load::Ready(results)) = &mut self.results {
            for a in results.iter_mut().filter(|a| a.id == detail.account.id) {
                *a = detail.account.clone();
            }
        }
        if self.account == Some(detail.account.id) {
            self.detail = Some(Load::Ready(detail));
        }
    }
}

/// The name an account goes by: its display name, else its username.
pub fn display_name(account: &AdminAccount) -> &str {
    account.global_name.as_deref().unwrap_or(&account.username)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn report(id: u128, reason: &str, status: &str) -> AdminContentReport {
        AdminContentReport {
            id: Uuid::from_u128(id),
            reason: reason.to_owned(),
            details: String::new(),
            reporter_email: None,
            status: status.to_owned(),
            created_at: chrono::Utc::now(),
            resolved_at: None,
            resolved_by_name: None,
            resolution_note: None,
            clip_id: None,
            clip_title: None,
            owner_id: None,
            owner_name: None,
            share_url: None,
            share_hidden: false,
            sharing_blocked: false,
            open_reports_on_clip: 1,
            owner_suspended: false,
            evidence: None,
        }
    }

    #[test]
    fn the_filters_keep_status_and_reason() {
        let mut a = Admin {
            open: Fetched::ready(vec![report(1, "terrorism", "open"), report(2, "copyright", "open")]),
            resolved: Fetched::ready(vec![report(3, "copyright", "dismissed")]),
            ..Admin::default()
        };
        assert_eq!(a.reports().len(), 2);
        assert_eq!(a.open_counts(), Some((2, 1)));
        a.reason = Some("copyright".to_owned());
        assert_eq!(a.reports()[0].id, Uuid::from_u128(2));
        a.status = StatusFilter::Dismissed;
        assert_eq!(a.reports()[0].id, Uuid::from_u128(3));
        a.status = StatusFilter::Removed;
        assert!(a.reports().is_empty());
    }

    #[test]
    fn a_report_dealt_with_moves_to_the_top_of_those_dealt_with() {
        let mut a = Admin {
            open: Fetched::ready(vec![report(1, "terrorism", "open"), report(2, "other", "open")]),
            resolved: Fetched::ready(vec![report(3, "other", "dismissed")]),
            ..Admin::default()
        };
        a.report_changed(report(1, "terrorism", "removed"));
        assert_eq!(a.open.value.as_ref().unwrap().len(), 1);
        assert_eq!(a.resolved.value.as_ref().unwrap()[0].status, "removed");
        a.report = Some(Uuid::from_u128(1));
        assert_eq!(a.selected_report().unwrap().status, "removed");
    }

    #[test]
    fn only_a_dismissal_goes_without_a_note() {
        let mut c = Confirm::new(Change::Remove {
            report: Uuid::nil(),
            clip: "Clip".to_owned(),
            owner: None,
        });
        assert!(!c.can_send());
        c.note.replace_text("  Confirmed  ");
        assert_eq!(c.note().as_deref(), Some("Confirmed"));
        assert!(c.can_send());
        let d = Confirm::new(Change::Dismiss {
            report: Uuid::nil(),
            clip: "Clip".to_owned(),
        });
        assert!(d.can_send());
        assert!(is_urgent("child_safety") && !is_urgent("copyright"));
    }
}
