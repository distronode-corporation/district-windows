//! W2 spike 2, the Rust half: a UniFFI async call that awaits a C# async
//! callback, over and over, and a shutdown while callbacks are in flight.
//!
//! The real boundary (`district-ffi`) has one async callback today,
//! `UiHost::open_url`, and calls it once per sign-in; that cannot show a
//! deadlock, a leak or a race at shutdown. This crate asks the same machinery
//! the same questions at volume, through the same versions of UniFFI and
//! uniffi-bindgen-cs. It is built and tested in CI only, never shipped.

uniffi::setup_scaffolding!();

use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, PoisonError};
use std::time::Duration;

use tokio::runtime::Runtime;

/// The C# side: answers each value with the next one, after awaiting
/// something of its own.
#[uniffi::export(with_foreign)]
#[async_trait::async_trait]
pub trait EchoHost: Send + Sync {
    /// Answers `value + 1`.
    async fn echo(&self, value: u64) -> u64;
}

/// Why a run stopped short.
#[derive(Debug, PartialEq, Eq, thiserror::Error, uniffi::Error)]
pub enum SpikeError {
    /// The host answered something other than `value + 1`.
    #[error("echo({value}) answered {answer}")]
    WrongAnswer {
        /// What was sent.
        value: u64,
        /// What came back.
        answer: u64,
    },
}

/// What a flood did before it was stopped.
#[derive(Clone, Copy, Debug, PartialEq, Eq, uniffi::Record)]
pub struct FloodReport {
    /// Callbacks started.
    pub started: u64,
    /// Callbacks whose answer arrived before the stop.
    pub answered: u64,
}

/// Awaits `host.echo(i)` for each `i` in `0..count`, one after another, and
/// checks every answer. Returns how many round trips completed.
#[uniffi::export]
pub async fn round_trips(host: Arc<dyn EchoHost>, count: u64) -> Result<u64, SpikeError> {
    for value in 0..count {
        let answer = host.echo(value).await;
        if answer != value + 1 {
            return Err(SpikeError::WrongAnswer { value, answer });
        }
    }
    Ok(count)
}

/// Callbacks kept in flight on a runtime of their own, until
/// [`stop`](Self::stop) drops them mid-await.
#[derive(uniffi::Object)]
pub struct Flood {
    runtime: Mutex<Option<Runtime>>,
    started: Arc<AtomicU64>,
    answered: Arc<AtomicU64>,
}

#[uniffi::export]
impl Flood {
    /// Starts `tasks` loops, each awaiting `host.echo` again as soon as the
    /// last one answers.
    #[uniffi::constructor]
    pub fn new(host: Arc<dyn EchoHost>, tasks: u32) -> Arc<Self> {
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(4)
            .enable_all()
            .build()
            .expect("a runtime for the flood");
        let started = Arc::new(AtomicU64::new(0));
        let answered = Arc::new(AtomicU64::new(0));
        for _ in 0..tasks {
            let host = Arc::clone(&host);
            let started = Arc::clone(&started);
            let answered = Arc::clone(&answered);
            runtime.spawn(async move {
                loop {
                    let value = started.fetch_add(1, Ordering::SeqCst);
                    host.echo(value).await;
                    answered.fetch_add(1, Ordering::SeqCst);
                }
            });
        }
        Arc::new(Self {
            runtime: Mutex::new(Some(runtime)),
            started,
            answered,
        })
    }

    /// How far the flood has got.
    pub fn report(&self) -> FloodReport {
        FloodReport {
            started: self.started.load(Ordering::SeqCst),
            answered: self.answered.load(Ordering::SeqCst),
        }
    }

    /// Stops the runtime with callbacks in flight: every pending
    /// `host.echo(..)` future is dropped while C# is still working on it. Waits
    /// up to `timeout_ms` for the runtime's threads, from an async call so the
    /// C# caller is not blocked.
    pub async fn stop(&self, timeout_ms: u64) -> FloodReport {
        let runtime = self
            .runtime
            .lock()
            .unwrap_or_else(PoisonError::into_inner)
            .take();
        if let Some(runtime) = runtime {
            let (done, finished) = tokio::sync::oneshot::channel();
            std::thread::spawn(move || {
                runtime.shutdown_timeout(Duration::from_millis(timeout_ms));
                done.send(()).ok();
            });
            finished.await.ok();
        }
        self.report()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    struct Echo {
        wrong_at: Option<u64>,
    }

    #[async_trait::async_trait]
    impl EchoHost for Echo {
        async fn echo(&self, value: u64) -> u64 {
            tokio::task::yield_now().await;
            if Some(value) == self.wrong_at {
                value
            } else {
                value + 1
            }
        }
    }

    #[tokio::test]
    async fn round_trips_check_every_answer() {
        let good = Arc::new(Echo { wrong_at: None });
        assert_eq!(round_trips(good, 1_000).await, Ok(1_000));
        let bad = Arc::new(Echo { wrong_at: Some(7) });
        assert_eq!(
            round_trips(bad, 1_000).await,
            Err(SpikeError::WrongAnswer {
                value: 7,
                answer: 7
            })
        );
    }

    #[tokio::test]
    async fn a_flood_stops_with_callbacks_in_flight() {
        let flood = Flood::new(Arc::new(Echo { wrong_at: None }), 8);
        tokio::time::sleep(Duration::from_millis(50)).await;
        let report = flood.stop(1_000).await;
        assert!(report.answered > 0);
        assert!(report.started >= report.answered);
        // A second stop finds nothing left to stop.
        assert_eq!(flood.stop(1_000).await, flood.report());
    }
}
