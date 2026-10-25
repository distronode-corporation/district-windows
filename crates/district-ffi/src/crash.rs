//! Crash reports from the Rust side of the app, off until the user says yes.
//!
//! The mechanism is the core's `district_crash`: nothing is set up, no panic
//! handler is installed and nothing is sent unless it is handed both a DSN and
//! the user's consent. The app owns the rest, in C# (`CrashReports` in
//! DistrictAI.Presentation, `CrashReporter` in the app): it keeps the answer,
//! asks once, shows the switch, and knows the DSN, which the release build
//! bakes into the app (`WINDOWS_SENTRY_DSN`); this crate reads nothing from its
//! own build or the environment. C# hands the answer to
//! [`crash_reports_apply`] at start (`Denied` while there is none) and again
//! whenever it changes, so a panic anywhere in district_ffi.dll is reported
//! from then on and turning the switch off stops it at once.
//!
//! The .NET side reports its own exceptions with the Sentry .NET SDK, driven
//! by the same answer; this boundary is only the Rust side's.

use std::time::Duration;

use district_crash::{Consent, CrashError, OffReason, Settings, Status};

/// What reports are about, as the app describes this build.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Record)]
pub struct CrashSettings {
    /// Where reports go: the DSN the release build carries. `None` or blank in
    /// every other build, and then nothing is ever set up.
    pub dsn: Option<String>,
    /// The app and its build, `com.distronode.DistrictAI@3.1.0+0123456789ab`:
    /// the package's version, then the first twelve characters of the commit
    /// it was built from. (No angle brackets here: this becomes a C# XML doc
    /// comment.)
    pub release: String,
    /// The environment reports are filed under: `production`.
    pub environment: String,
}

/// The user's answer. `Denied` is what the app passes until the user has
/// said yes.
#[derive(Clone, Copy, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum CrashConsent {
    /// Crash reports are on.
    Granted,
    /// Crash reports are off: not answered yet, "Don't send", or the switch
    /// turned off.
    Denied,
}

/// Whether reports are sent after [`crash_reports_apply`].
#[derive(Clone, Copy, Debug, PartialEq, Eq, uniffi::Enum)]
pub enum CrashStatus {
    /// On: a panic is reported.
    On,
    /// Off: the user has not said yes.
    OffNoConsent,
    /// Off: this build has no DSN.
    OffNoDsn,
}

/// A DSN the reporter will not use. Reporting is off after either.
#[derive(Clone, Copy, Debug, PartialEq, Eq, thiserror::Error, uniffi::Error)]
pub enum CrashReportsError {
    /// The DSN is not one.
    #[error("the crash report DSN is not a valid DSN")]
    InvalidDsn,
    /// The DSN is plain `http` to another machine.
    #[error("the crash report DSN is plain http to another machine")]
    InsecureDsn,
}

impl From<CrashError> for CrashReportsError {
    fn from(error: CrashError) -> Self {
        match error {
            CrashError::InvalidDsn => Self::InvalidDsn,
            CrashError::InsecureDsn => Self::InsecureDsn,
        }
    }
}

/// Turns the Rust side's reporting on or off for this process, and says which
/// it is: on only with `Granted` and a DSN. Whatever was on before is closed
/// first, after what it had queued is sent (up to two seconds).
///
/// # Errors
///
/// [`CrashReportsError`] when consent is granted but the DSN is unusable;
/// reporting is then off.
#[uniffi::export]
pub fn crash_reports_apply(
    settings: CrashSettings,
    consent: CrashConsent,
) -> Result<CrashStatus, CrashReportsError> {
    let settings = Settings {
        dsn: settings.dsn,
        release: settings.release,
        environment: settings.environment,
    };
    let consent = match consent {
        CrashConsent::Granted => Consent::Granted,
        CrashConsent::Denied => Consent::Denied,
    };
    Ok(match district_crash::apply(&settings, consent)? {
        Status::On => CrashStatus::On,
        Status::Off(OffReason::NoConsent) => CrashStatus::OffNoConsent,
        Status::Off(OffReason::NoDsn) => CrashStatus::OffNoDsn,
    })
}

/// Whether the Rust side is reporting now.
#[uniffi::export]
pub fn crash_reports_enabled() -> bool {
    district_crash::is_enabled()
}

/// Waits up to `timeout_ms` milliseconds for queued reports to be sent, as the
/// app quits; `true` when nothing is left, and when reporting is off.
#[uniffi::export]
pub fn crash_reports_flush(timeout_ms: u32) -> bool {
    district_crash::flush(Duration::from_millis(u64::from(timeout_ms)))
}

#[cfg(test)]
mod tests {
    use std::sync::{Mutex, PoisonError};

    use super::*;

    /// The reporter is one per process: the tests that turn it on and off take
    /// turns.
    static ONE_AT_A_TIME: Mutex<()> = Mutex::new(());

    fn settings(dsn: Option<&str>) -> CrashSettings {
        CrashSettings {
            dsn: dsn.map(str::to_owned),
            release: "com.distronode.DistrictAI@3.1.0+0123456789ab".to_owned(),
            environment: "production".to_owned(),
        }
    }

    /// No DSN, whatever the answer, and no consent, whatever the DSN: nothing
    /// is set up. This is every build but a release.
    #[test]
    fn off_without_a_dsn_or_without_consent() {
        let _turn = ONE_AT_A_TIME.lock().unwrap_or_else(PoisonError::into_inner);
        assert_eq!(
            crash_reports_apply(settings(None), CrashConsent::Granted),
            Ok(CrashStatus::OffNoDsn)
        );
        assert_eq!(
            crash_reports_apply(settings(Some("  ")), CrashConsent::Granted),
            Ok(CrashStatus::OffNoDsn)
        );
        assert!(!crash_reports_enabled());
        assert_eq!(
            crash_reports_apply(
                settings(Some("https://key@example.com/1")),
                CrashConsent::Denied
            ),
            Ok(CrashStatus::OffNoConsent)
        );
        assert!(!crash_reports_enabled());
        assert!(crash_reports_flush(10));
    }

    /// A DSN the reporter refuses is an error, and reporting stays off.
    #[test]
    fn a_dsn_it_will_not_use_is_an_error() {
        let _turn = ONE_AT_A_TIME.lock().unwrap_or_else(PoisonError::into_inner);
        assert_eq!(
            crash_reports_apply(settings(Some("not a dsn")), CrashConsent::Granted),
            Err(CrashReportsError::InvalidDsn)
        );
        assert_eq!(
            crash_reports_apply(
                settings(Some("http://key@example.com/1")),
                CrashConsent::Granted
            ),
            Err(CrashReportsError::InsecureDsn)
        );
        assert!(!crash_reports_enabled());
        assert_eq!(
            CrashReportsError::InvalidDsn.to_string(),
            "the crash report DSN is not a valid DSN"
        );
        assert_eq!(
            CrashReportsError::InsecureDsn.to_string(),
            "the crash report DSN is plain http to another machine"
        );
    }

    /// Consent and a DSN turn it on, and Denied turns it off again at once.
    /// The DSN names this machine's discard port, so nothing could leave it;
    /// nothing is sent by turning reporting on or off in any case.
    #[test]
    fn granted_with_a_dsn_is_on_until_denied() {
        let _turn = ONE_AT_A_TIME.lock().unwrap_or_else(PoisonError::into_inner);
        let local = settings(Some("http://key@127.0.0.1:9/1"));
        assert_eq!(
            crash_reports_apply(local.clone(), CrashConsent::Granted),
            Ok(CrashStatus::On)
        );
        assert!(crash_reports_enabled());
        assert!(crash_reports_flush(10));
        assert_eq!(
            crash_reports_apply(local, CrashConsent::Denied),
            Ok(CrashStatus::OffNoConsent)
        );
        assert!(!crash_reports_enabled());
    }
}
