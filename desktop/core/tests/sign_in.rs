//! Signing in with a device code. Ported from the .NET core's `SignInTests`; its tests of the HTTP handler (the token
//! and client headers, and 401 and 426) belong to the API client and live in `reelshelf-api`.

mod support;

use std::sync::Arc;
use std::time::Duration;

use chrono::TimeDelta;
use reelshelf_core::api::DeviceAuthorization;
use reelshelf_core::platform::Clock;
use reelshelf_core::sign_in::{DeviceSignIn, PendingSignIn, SignInOutcome};
use support::{FakeClock, FakeReelshelf, MemoryCredentials, oauth_error, problem};

struct SignIn {
    clock: Arc<FakeClock>,
    server: Arc<FakeReelshelf>,
    credentials: Arc<MemoryCredentials>,
    sign_in: DeviceSignIn<FakeReelshelf>,
}

fn create() -> SignIn {
    let clock = Arc::new(FakeClock::new());
    let server = Arc::new(FakeReelshelf::new(clock.clone()));
    let credentials = Arc::new(MemoryCredentials::new(None));
    let sign_in = DeviceSignIn::new(server.clone(), credentials.clone(), clock.clone());
    SignIn {
        clock,
        server,
        credentials,
        sign_in,
    }
}

fn pending(clock: &FakeClock) -> PendingSignIn {
    PendingSignIn {
        device_code: "device-code".to_string(),
        user_code: "KDRW-TPLM".to_string(),
        verification_uri: "https://reelshelf.app/device".to_string(),
        verification_uri_complete: "https://reelshelf.app/device?code=KDRW-TPLM".to_string(),
        interval: Duration::from_secs(5),
        expires_at: clock.now() + TimeDelta::minutes(10),
    }
}

#[tokio::test]
async fn signing_in_polls_at_the_interval_slows_down_when_asked_and_stores_the_token() {
    let test = create();
    {
        let mut state = test.server.state();
        state.authorize_answers.push_back(Ok(DeviceAuthorization {
            device_code: "device-code".to_string(),
            user_code: "KDRW-TPLM".to_string(),
            verification_uri: "https://reelshelf.app/device".to_string(),
            verification_uri_complete: "https://reelshelf.app/device?code=KDRW-TPLM".to_string(),
            interval: Some(5),
            expires_in: Some(600),
        }));
        state.token_answers.extend([
            Err(oauth_error("authorization_pending")),
            Err(oauth_error("slow_down")),
            Err(oauth_error("authorization_pending")),
            Ok("rsd_issued".to_string()),
        ]);
    }

    let SignInOutcome::Started(started) = test.sign_in.start("Gaming PC").await else {
        panic!("the sign-in should have started");
    };
    assert_eq!(started.user_code, "KDRW-TPLM");
    assert_eq!(
        started.verification_uri_complete,
        "https://reelshelf.app/device?code=KDRW-TPLM"
    );
    assert_eq!(test.server.state().device_names, ["Gaming PC"]);

    assert_eq!(test.sign_in.wait_for_approval(&started).await, SignInOutcome::SignedIn);

    assert_eq!(test.credentials.token().as_deref(), Some("rsd_issued"));
    assert_eq!(test.clock.delays(), [5, 5, 10, 10].map(Duration::from_secs));
    assert!(
        test.server
            .state()
            .polled_codes
            .iter()
            .all(|code| code == "device-code")
    );
}

#[tokio::test]
async fn a_denied_or_expired_code_stops_polling() {
    for (error, outcome) in [
        ("access_denied", SignInOutcome::Denied),
        ("expired_token", SignInOutcome::Expired),
    ] {
        let test = create();
        test.server.state().token_answers.push_back(Err(oauth_error(error)));

        assert_eq!(
            test.sign_in.wait_for_approval(&pending(&test.clock)).await,
            outcome,
            "{error}"
        );
        assert_eq!(test.credentials.token(), None);
    }
}

#[tokio::test]
async fn a_code_that_outlives_its_time_expires_without_an_answer() {
    let test = create();
    test.server
        .state()
        .token_answers
        .extend((0..200).map(|_| Err(oauth_error("authorization_pending"))));

    assert_eq!(
        test.sign_in.wait_for_approval(&pending(&test.clock)).await,
        SignInOutcome::Expired
    );
    assert_eq!(test.server.state().polled_codes.len(), 119);
}

#[tokio::test]
async fn an_old_version_or_no_answer_stops_the_sign_in_before_it_starts() {
    let test = create();
    test.server
        .state()
        .authorize_answers
        .push_back(Err(problem(426, None, None)));
    assert_eq!(test.sign_in.start("Gaming PC").await, SignInOutcome::UpdateRequired);

    assert_eq!(test.sign_in.start("Gaming PC").await, SignInOutcome::Unreachable);
}
