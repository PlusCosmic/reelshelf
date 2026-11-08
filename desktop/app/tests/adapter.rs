//! The engine's API on the real client, against a mock server: the stored token goes with each call, the types map
//! both ways, and every refusal becomes the core's `SignedOut`, `UpdateRequired` or `Status` with its problem.

use std::collections::BTreeMap;
use std::sync::Arc;

use bytes::Bytes;
use chrono::{TimeZone, Utc};
use reelshelf_api::ApiClient;
use reelshelf_app::api_adapter::{ReelshelfApi, map_error};
use reelshelf_app::platform::MemoryCredentials;
use reelshelf_core::api::{ApiError, PartBody, PrepareClip, Problem, UploaderApi};
use reelshelf_core::platform::CredentialStore;
use serde_json::json;
use uuid::Uuid;
use wiremock::matchers::{body_json, header, header_exists, method, path, query_param};
use wiremock::{Mock, MockServer, Request, ResponseTemplate};

const TOKEN: &str = "rsd_adapter-token";

fn api(server: &MockServer, credentials: Arc<MemoryCredentials>) -> ReelshelfApi {
    ReelshelfApi::new(ApiClient::new(&server.uri(), "1.2.3").unwrap(), credentials)
}

fn signed_in(server: &MockServer) -> ReelshelfApi {
    api(server, Arc::new(MemoryCredentials::with_token(TOKEN)))
}

fn plan_body() -> serde_json::Value {
    json!({
        "tier": "free", "clip_count": 3, "clip_limit": 100, "new_clips_last30_days": 7, "new_clip_limit": 200,
        "max_clip_seconds": 60, "storage_used_bytes": 1024, "has_transcripts": false, "billing_enabled": true,
        "can_manage_billing": false, "subscription": null, "clips_over_limit": 0, "over_limit_deletes_at": null,
        "free_clip_limit": 100, "over_limit_grace_days": 14, "can_change_plan": false, "granted_tier": null
    })
}

async fn plan_answering(status: u16, body: serde_json::Value) -> ApiError {
    let server = MockServer::start().await;
    Mock::given(method("GET"))
        .and(path("/api/me/plan"))
        .respond_with(ResponseTemplate::new(status).set_body_json(body))
        .mount(&server)
        .await;
    signed_in(&server).plan().await.unwrap_err()
}

#[tokio::test]
async fn each_call_carries_the_token_the_store_holds_now() {
    let server = MockServer::start().await;
    Mock::given(method("GET"))
        .and(path("/api/me/plan"))
        .and(header("authorization", format!("Bearer {TOKEN}").as_str()))
        .respond_with(ResponseTemplate::new(200).set_body_json(plan_body()))
        .expect(1)
        .mount(&server)
        .await;
    let credentials = Arc::new(MemoryCredentials::default());
    let api = api(&server, credentials.clone());

    // Signed out: no token goes, and this server answers that with a 404.
    assert!(api.plan().await.is_err());
    // Sign-in writes the token to the store; the next call carries it.
    credentials.write_token(TOKEN);
    let plan = api.plan().await.unwrap();
    assert_eq!(
        (
            plan.clip_count,
            plan.clip_limit,
            plan.new_clips,
            plan.new_clip_limit,
            plan.max_clip_seconds
        ),
        (3, Some(100), 7, Some(200), Some(60))
    );
    // Sign-out deletes it; the next call goes without.
    credentials.delete_token();
    let requests = server.received_requests().await.unwrap();
    assert!(!requests[0].headers.contains_key("authorization"));
}

#[tokio::test]
async fn sign_in_maps_the_code_and_the_polls_answers() {
    let server = MockServer::start().await;
    Mock::given(method("POST"))
        .and(path("/api/devices/authorize"))
        .and(body_json(json!({ "device_name": "Gaming PC" })))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "device_code": "dc", "user_code": "MFNDVRWZ", "verification_uri": "https://reelshelf.app/device",
            "verification_uri_complete": "https://reelshelf.app/device?code=MFND-VRWZ", "interval": 5, "expires_in": 600
        })))
        .mount(&server)
        .await;
    let api = api(&server, Arc::new(MemoryCredentials::default()));
    let started = api.authorize("Gaming PC").await.unwrap();
    assert_eq!(started.device_code, "dc");
    assert_eq!(started.user_code, "MFNDVRWZ");
    assert_eq!(started.verification_uri, "https://reelshelf.app/device");
    assert_eq!(
        started.verification_uri_complete,
        "https://reelshelf.app/device?code=MFND-VRWZ"
    );
    assert_eq!((started.interval, started.expires_in), (Some(5), Some(600)));

    for (code, expected) in [
        ("authorization_pending", "authorization_pending"),
        ("slow_down", "slow_down"),
        ("access_denied", "access_denied"),
        ("expired_token", "expired_token"),
        ("something_new", "something_new"),
    ] {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/devices/token"))
            .respond_with(ResponseTemplate::new(400).set_body_json(json!({ "error": code })))
            .mount(&server)
            .await;
        let error = api_for(&server).poll_token("dc").await.unwrap_err();
        assert_eq!(
            error,
            ApiError::Status(Problem {
                error: Some(expected.to_owned()),
                ..Problem::new(400)
            }),
            "{code}"
        );
    }

    let server = MockServer::start().await;
    Mock::given(method("POST"))
        .and(path("/api/devices/token"))
        .respond_with(
            ResponseTemplate::new(200).set_body_json(json!({ "access_token": TOKEN, "token_type": "Bearer" })),
        )
        .mount(&server)
        .await;
    assert_eq!(api_for(&server).poll_token("dc").await.unwrap(), TOKEN);
}

fn api_for(server: &MockServer) -> ReelshelfApi {
    api(server, Arc::new(MemoryCredentials::default()))
}

#[tokio::test]
async fn books_keep_their_slug_and_cloth() {
    let server = MockServer::start().await;
    let id = Uuid::new_v4();
    Mock::given(method("GET"))
        .and(path("/api/games/categories"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!([{
            "id": id, "name": "Apex Legends", "slug": "apex-legends", "cover_url": null, "key_art_url": null,
            "game_logo_url": null, "is_custom": false, "cloth_color": "#a34d4d"
        }])))
        .mount(&server)
        .await;
    let books = signed_in(&server).books().await.unwrap();
    assert_eq!(books.len(), 1);
    assert_eq!(books[0].id, id);
    assert_eq!(books[0].name, "Apex Legends");
    assert_eq!(books[0].slug.as_deref(), Some("apex-legends"));
    assert_eq!(books[0].cloth_color.as_deref(), Some("#a34d4d"));
}

#[tokio::test]
async fn preparing_and_uploading_a_clip_map_both_ways() {
    let server = MockServer::start().await;
    let book = Uuid::new_v4();
    let clip = Uuid::new_v4();
    Mock::given(method("POST"))
        .and(path(format!("/api/clips/categories/{book}/videos")))
        .and(query_param("videoTitle", "Apex Legends 2026.11.08 - 21.14.03"))
        .and(query_param("fileSize", "90177536"))
        .and(query_param("md5Hash", "0123456789abcdef0123456789abcdef"))
        .and(query_param("createdAt", "2026-11-08T21:14:03Z"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "clip_id": clip, "video_id": clip,
            "upload": {
                "protocol": "s3-multipart", "endpoint": format!("/api/clips/videos/{clip}/upload"),
                "headers": {}, "metadata": {}, "expires_at": "2026-11-09T12:30:00Z",
                "multipart": { "upload_id": "up-1", "part_size": 8388608, "part_count": 2 }
            }
        })))
        .expect(1)
        .mount(&server)
        .await;
    let base = format!("/api/clips/videos/{clip}/upload");
    Mock::given(method("POST"))
        .and(path(format!("{base}/parts")))
        .and(body_json(json!({ "upload_id": "up-1", "part_numbers": [1, 2] })))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "parts": [{ "part_number": 1, "url": "https://r2/1" }, { "part_number": 2, "url": "https://r2/2" }],
            "expires_at": "2026-11-08T13:00:00Z"
        })))
        .mount(&server)
        .await;
    Mock::given(method("GET"))
        .and(path(format!("{base}/parts")))
        .and(query_param("uploadId", "up-1"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "parts": [{ "part_number": 1, "etag": "\"e1\"", "size": 8388608 }]
        })))
        .mount(&server)
        .await;
    Mock::given(method("POST"))
        .and(path(format!("{base}/complete")))
        .and(body_json(json!({
            "upload_id": "up-1",
            "parts": [{ "part_number": 1, "etag": "\"e1\"" }, { "part_number": 2, "etag": "\"e2\"" }]
        })))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({ "clip_id": clip, "status": "uploaded" })))
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(method("DELETE"))
        .and(path(format!("/api/clips/videos/{clip}")))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let api = signed_in(&server);

    let prepared = api
        .prepare(&PrepareClip {
            category_id: book,
            title: "Apex Legends 2026.11.08 - 21.14.03".into(),
            file_size: 90_177_536,
            created_at: Utc.with_ymd_and_hms(2026, 11, 8, 21, 14, 3).unwrap(),
            md5: "0123456789abcdef0123456789abcdef".into(),
        })
        .await
        .unwrap();
    assert_eq!(prepared.clip_id, clip);
    assert_eq!(prepared.upload.protocol, "s3-multipart");
    let multipart = prepared.upload.multipart.unwrap();
    assert_eq!(
        (multipart.upload_id.as_str(), multipart.part_size, multipart.part_count),
        ("up-1", 8_388_608, 2)
    );

    let presigned = api.presign_parts(clip, "up-1", &[1, 2]).await.unwrap();
    assert_eq!(presigned.parts.len(), 2);
    assert_eq!(presigned.parts[1].url, "https://r2/2");
    assert_eq!(
        presigned.expires_at,
        Some(Utc.with_ymd_and_hms(2026, 11, 8, 13, 0, 0).unwrap())
    );

    let held = api.list_parts(clip, "up-1").await.unwrap();
    assert_eq!(
        (held[0].part_number, held[0].etag.as_str(), held[0].size),
        (1, "\"e1\"", 8_388_608)
    );

    let parts = BTreeMap::from([(2, "\"e2\"".to_owned()), (1, "\"e1\"".to_owned())]);
    api.complete(clip, "up-1", &parts).await.unwrap();
    api.delete_clip(clip).await.unwrap();
}

fn body(bytes: &'static [u8]) -> PartBody {
    Box::pin(futures_util::stream::iter([Ok::<_, std::io::Error>(
        Bytes::from_static(bytes),
    )]))
}

#[tokio::test]
async fn parts_go_to_storage_without_the_token_and_answer_their_etag() {
    let server = MockServer::start().await;
    Mock::given(method("PUT"))
        .and(path("/part/1"))
        .respond_with(ResponseTemplate::new(200).insert_header("ETag", "\"e1\""))
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/part/2"))
        .respond_with(ResponseTemplate::new(200))
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/part/3"))
        .respond_with(ResponseTemplate::new(500))
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(header_exists("authorization"))
        .respond_with(ResponseTemplate::new(418))
        .with_priority(1)
        .mount(&server)
        .await;
    let api = signed_in(&server);
    let url = |n: u32| format!("{}/part/{n}", server.uri());

    assert_eq!(
        api.put_part(&url(1), 5, body(b"hello")).await.unwrap().as_deref(),
        Some("\"e1\"")
    );
    assert_eq!(api.put_part(&url(2), 5, body(b"hello")).await.unwrap(), None);
    let error = api.put_part(&url(3), 5, body(b"hello")).await.unwrap_err();
    assert_eq!(error, ApiError::Status(Problem::new(500)));
    assert!(error.is_transient());
    let sent: Vec<Request> = server.received_requests().await.unwrap();
    assert_eq!(sent[0].body, b"hello");
}

#[tokio::test]
async fn refusals_map_onto_the_cores_errors() {
    assert_eq!(plan_answering(401, json!({})).await, ApiError::SignedOut);
    assert_eq!(
        plan_answering(426, json!({ "error": "client_too_old", "minimum_version": "1.3" })).await,
        ApiError::UpdateRequired
    );
    for status in [403, 404, 409, 422, 503, 500, 429] {
        let error = plan_answering(
            status,
            json!({ "type": null, "title": "Plan Limit Reached", "status": status, "detail": "Full" }),
        )
        .await;
        assert_eq!(
            error,
            ApiError::Status(Problem {
                status,
                title: Some("Plan Limit Reached".into()),
                detail: Some("Full".into()),
                error: None,
            }),
            "{status}"
        );
        assert_eq!(error.is_transient(), status >= 500 || status == 429, "{status}");
    }
    // A success this version can't read is tried again later, as if no answer came.
    let error = plan_answering(200, json!({ "tier": 3 })).await;
    assert!(matches!(error, ApiError::Network(_)), "{error:?}");
}

#[tokio::test]
async fn no_server_is_a_network_error() {
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let uri = format!("http://{}", listener.local_addr().unwrap());
    drop(listener);
    let api = ReelshelfApi::new(
        ApiClient::new(&uri, "1.2.3").unwrap(),
        Arc::new(MemoryCredentials::default()),
    );
    let error = api.books().await.unwrap_err();
    assert!(matches!(error, ApiError::Network(_)), "{error:?}");
    assert!(error.is_transient());
    assert!(matches!(
        map_error(reelshelf_api::ApiError::MissingEtag),
        ApiError::Network(_)
    ));
}
