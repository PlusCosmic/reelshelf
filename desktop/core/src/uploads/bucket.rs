use std::sync::{Arc, Mutex};
use std::time::Duration;

use chrono::{DateTime, Utc};

use crate::platform::Clock;

struct Bucket {
    bytes_per_second: Option<u64>,
    tokens: f64,
    last: DateTime<Utc>,
}

/// The upload speed limit: a token bucket refilled at the limit, holding at most a second's worth, so a read waits
/// until the bytes it took have been earned. Without a rate nothing waits.
pub struct TokenBucket {
    clock: Arc<dyn Clock>,
    bucket: Mutex<Bucket>,
}

impl TokenBucket {
    pub fn new(clock: Arc<dyn Clock>) -> Self {
        let last = clock.now();
        TokenBucket {
            clock,
            bucket: Mutex::new(Bucket {
                bytes_per_second: None,
                tokens: 0.0,
                last,
            }),
        }
    }

    /// Megabits a second as bytes a second; None (or 0) for no limit.
    pub fn from_mbps(mbps: Option<u32>) -> Option<u64> {
        mbps.filter(|&value| value > 0)
            .map(|value| u64::from(value) * 1_000_000 / 8)
    }

    fn bucket(&self) -> std::sync::MutexGuard<'_, Bucket> {
        self.bucket.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    pub fn bytes_per_second(&self) -> Option<u64> {
        self.bucket().bytes_per_second
    }

    pub fn set_rate(&self, bytes_per_second: Option<u64>) {
        let mut bucket = self.bucket();
        if bucket.bytes_per_second == bytes_per_second {
            return;
        }
        bucket.bytes_per_second = bytes_per_second.filter(|&rate| rate > 0);
        bucket.tokens = 0.0;
        bucket.last = self.clock.now();
    }

    /// Takes `bytes`, waiting first when the bucket is in debt.
    pub async fn take(&self, bytes: usize) {
        let wait = {
            let mut bucket = self.bucket();
            let Some(rate) = bucket.bytes_per_second else {
                return;
            };
            let rate = rate as f64;
            let now = self.clock.now();
            let elapsed = (now - bucket.last).to_std().unwrap_or_default().as_secs_f64();
            bucket.tokens = rate.min(bucket.tokens + elapsed * rate);
            bucket.last = now;
            bucket.tokens -= bytes as f64;
            if bucket.tokens < 0.0 {
                Duration::from_secs_f64(-bucket.tokens / rate)
            } else {
                Duration::ZERO
            }
        };

        if !wait.is_zero() {
            self.clock.sleep(wait).await;
        }
    }
}
