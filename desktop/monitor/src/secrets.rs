//! Where the app keeps what it must not lose or show: Reelshelf's device token, and Grafana's address and token.
//!
//! On macOS that is the login Keychain, as generic passwords under the service [`SERVICE`]. Everywhere else (Linux,
//! where CI runs the tests) it is [`MemorySecrets`], kept until the app quits, as the uploader's stand-ins are.

use std::collections::HashMap;
use std::sync::Mutex;

/// The Keychain service every item is kept under.
pub const SERVICE: &str = "app.reelshelf.monitor";

/// One thing kept.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub enum Secret {
    /// The `rsd_` device token for the site it was issued by, so a debug build pointed at dev keeps its own.
    DeviceToken {
        site: String,
    },
    GrafanaUrl,
    GrafanaToken,
}

impl Secret {
    /// The item's account name in the Keychain.
    pub fn account(&self) -> String {
        match self {
            Secret::DeviceToken { site } => format!("device-token {site}"),
            Secret::GrafanaUrl => "grafana-url".to_owned(),
            Secret::GrafanaToken => "grafana-token".to_owned(),
        }
    }
}

pub trait Secrets: Send + Sync {
    fn read(&self, secret: &Secret) -> Option<String>;
    /// Keeps `value`, answering why it couldn't.
    fn write(&self, secret: &Secret, value: &str) -> Result<(), String>;
    fn delete(&self, secret: &Secret);
}

/// The Keychain on macOS, the memory elsewhere.
pub fn native() -> std::sync::Arc<dyn Secrets> {
    #[cfg(target_os = "macos")]
    {
        std::sync::Arc::new(keychain::Keychain)
    }
    #[cfg(not(target_os = "macos"))]
    {
        std::sync::Arc::new(MemorySecrets::default())
    }
}

/// Kept in memory, gone when the app quits.
#[derive(Default)]
pub struct MemorySecrets(Mutex<HashMap<Secret, String>>);

impl MemorySecrets {
    fn items(&self) -> std::sync::MutexGuard<'_, HashMap<Secret, String>> {
        self.0.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
    }
}

impl Secrets for MemorySecrets {
    fn read(&self, secret: &Secret) -> Option<String> {
        self.items().get(secret).cloned()
    }

    fn write(&self, secret: &Secret, value: &str) -> Result<(), String> {
        self.items().insert(secret.clone(), value.to_owned());
        Ok(())
    }

    fn delete(&self, secret: &Secret) {
        self.items().remove(secret);
    }
}

#[cfg(target_os = "macos")]
mod keychain {
    use security_framework::passwords::{delete_generic_password, get_generic_password, set_generic_password};

    use super::{SERVICE, Secret, Secrets};

    /// The login Keychain's generic passwords.
    pub struct Keychain;

    impl Secrets for Keychain {
        fn read(&self, secret: &Secret) -> Option<String> {
            let bytes = get_generic_password(SERVICE, &secret.account()).ok()?;
            String::from_utf8(bytes).ok().filter(|value| !value.is_empty())
        }

        fn write(&self, secret: &Secret, value: &str) -> Result<(), String> {
            set_generic_password(SERVICE, &secret.account(), value.as_bytes()).map_err(|error| error.to_string())
        }

        fn delete(&self, secret: &Secret) {
            let _ = delete_generic_password(SERVICE, &secret.account());
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_memory_keeps_each_secret_apart() {
        let secrets = MemorySecrets::default();
        let dev = Secret::DeviceToken {
            site: "https://dev.example".to_owned(),
        };
        let prod = Secret::DeviceToken {
            site: "https://reelshelf.app".to_owned(),
        };
        secrets.write(&dev, "rsd_dev").unwrap();
        assert_eq!(secrets.read(&dev).as_deref(), Some("rsd_dev"));
        assert_eq!(secrets.read(&prod), None);
        secrets.delete(&dev);
        assert_eq!(secrets.read(&dev), None);
        assert_ne!(dev.account(), prod.account());
    }
}
