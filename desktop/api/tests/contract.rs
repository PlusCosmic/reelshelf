//! Checks the client against the API's OpenAPI document (`frontend/src/api-client/Reelshelf.json`, kept current by
//! `bun run check:api-client-drift`), so an API change that would break the app fails `cargo test`:
//!
//! - each endpoint the client calls exists with its method and `operationId`, and its query parameters are the
//!   document's;
//! - each call's request body and success response use the schemas the table below names;
//! - every field of each Rust type is a property of its schema under the same JSON name, and every required property
//!   is a field (the names come from the types' own `Deserialize`, so they can't drift from what serde reads);
//! - every object schema a mapped schema refers to is mapped too, and an enum's variants are the schema's values;
//! - examples built from each response schema, one with every property and one with only the required ones (nulls
//!   wherever the schema allows them), deserialize into the Rust type.
//!
//! `REELSHELF_OPENAPI` points the test at another copy of the document.

use std::collections::BTreeSet;
use std::fmt;
use std::path::PathBuf;

use reelshelf_api::endpoints::{self, Endpoint};
use reelshelf_api::*;
use serde::de::{self, DeserializeOwned, Visitor};
use serde::forward_to_deserialize_any;
use serde_json::{Map, Value, json};

/// What a type's `Deserialize` asks for: a struct's fields or an enum's variants, by their JSON names.
#[derive(Debug)]
enum Shape {
    Struct(&'static [&'static str]),
    Enum(&'static [&'static str]),
    Other(String),
}

impl fmt::Display for Shape {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{self:?}")
    }
}

impl std::error::Error for Shape {}

impl de::Error for Shape {
    fn custom<T: fmt::Display>(message: T) -> Self {
        Shape::Other(message.to_string())
    }
}

/// A deserializer that reads nothing: it fails at once with the field or variant names the type asked for.
struct Recorder;

impl<'de> de::Deserializer<'de> for Recorder {
    type Error = Shape;

    fn deserialize_any<V: Visitor<'de>>(self, _visitor: V) -> Result<V::Value, Shape> {
        Err(Shape::Other("not a struct or an enum".into()))
    }

    fn deserialize_struct<V: Visitor<'de>>(
        self,
        _name: &'static str,
        fields: &'static [&'static str],
        _visitor: V,
    ) -> Result<V::Value, Shape> {
        Err(Shape::Struct(fields))
    }

    fn deserialize_enum<V: Visitor<'de>>(
        self,
        _name: &'static str,
        variants: &'static [&'static str],
        _visitor: V,
    ) -> Result<V::Value, Shape> {
        Err(Shape::Enum(variants))
    }

    forward_to_deserialize_any! {
        bool i8 i16 i32 i64 i128 u8 u16 u32 u64 u128 f32 f64 char str string bytes byte_buf option unit
        unit_struct newtype_struct seq tuple tuple_struct map identifier ignored_any
    }
}

fn shape<T: DeserializeOwned>() -> Shape {
    match T::deserialize(Recorder) {
        Ok(_) => Shape::Other("deserialized from nothing".into()),
        Err(shape) => shape,
    }
}

fn decodes<T: DeserializeOwned>(value: &Value) -> Result<(), String> {
    serde_json::from_value::<T>(value.clone())
        .map(|_| ())
        .map_err(|error| error.to_string())
}

/// Deserializes a JSON value into a type, answering why it couldn't.
type Decode = fn(&Value) -> Result<(), String>;

/// A Rust type and the schema it mirrors.
struct TypeMap {
    rust: &'static str,
    schema: &'static str,
    shape: fn() -> Shape,
    /// For types the client reads: deserializes an example built from the schema.
    decode: Option<Decode>,
}

macro_rules! sent {
    ($rust:ty => $schema:literal) => {
        TypeMap {
            rust: stringify!($rust),
            schema: $schema,
            shape: shape::<$rust>,
            decode: None,
        }
    };
}

macro_rules! read {
    ($rust:ty => $schema:literal) => {
        TypeMap {
            rust: stringify!($rust),
            schema: $schema,
            shape: shape::<$rust>,
            decode: Some(decodes::<$rust>),
        }
    };
}

/// Every Rust type the client sends or reads, by its schema.
fn types() -> Vec<TypeMap> {
    vec![
        sent!(DeviceAuthorizeRequest => "DeviceAuthorizeRequest"),
        read!(DeviceAuthorization => "DeviceAuthorizationResponse"),
        sent!(DeviceTokenRequest => "DeviceTokenRequest"),
        read!(DeviceToken => "DeviceTokenResponse"),
        read!(DeviceTokenErrorBody => "DeviceTokenError"),
        read!(GameCategory => "GameCategoryResponse"),
        read!(AccountPlan => "AccountPlanResponse"),
        read!(PlanSubscription => "PlanSubscription"),
        read!(CreateClipResponse => "CreateClipResponse"),
        read!(UploadTarget => "UploadTarget"),
        read!(MultipartUploadInfo => "MultipartUploadInfo"),
        sent!(PresignUploadPartsRequest => "PresignUploadPartsRequest"),
        read!(PresignedUploadParts => "PresignedUploadParts"),
        read!(PresignedUploadPart => "PresignedUploadPart"),
        read!(UploadedParts => "UploadedParts"),
        read!(UploadedPart => "UploadedPart"),
        sent!(CompleteUploadRequest => "CompleteUploadRequest"),
        sent!(CompletedUploadPart => "CompletedUploadPart"),
        read!(CompletedUpload => "CompletedUpload"),
        read!(ClipVideoStatus => "ClipVideoStatus"),
        read!(CurrentUser => "CurrentUserResponse"),
        read!(FeatureFlags => "FeatureFlagsResponse"),
        read!(OpsStorage => "OpsStorageResponse"),
        read!(OpsObjectStore => "OpsObjectStore"),
        read!(OpsStorageTotals => "OpsStorageTotals"),
        read!(OpsTierStorage => "OpsTierStorage"),
        read!(OpsAccountStorage => "OpsAccountStorage"),
        read!(OpsExportStorage => "OpsExportStorage"),
        read!(OpsQueueCounts => "OpsQueueCounts"),
        read!(OpsFinishedCounts => "OpsFinishedCounts"),
        read!(OpsTranscoding => "OpsTranscodingResponse"),
        read!(OpsTranscodeFailure => "OpsTranscodeFailure"),
        read!(OpsOverflow => "OpsOverflow"),
        read!(OpsOverflowPool => "OpsOverflowPool"),
        read!(OpsAacBackfill => "OpsAacBackfill"),
        read!(OpsAi => "OpsAiResponse"),
        read!(OpsAiQueue => "OpsAiQueue"),
        read!(OpsAiFailure => "OpsAiFailure"),
        read!(OpsAiSpend => "OpsAiSpend"),
        read!(OpsAiUsage => "OpsAiUsage"),
        read!(AdminAccount => "AdminAccount"),
        read!(AdminAccountIdentity => "AdminAccountIdentity"),
        read!(AdminContentReport => "AdminContentReport"),
        read!(AdminContentReportEvidence => "AdminContentReportEvidence"),
    ]
}

/// A response body: one schema, or an array of it.
enum Body {
    One(&'static str),
    Array(&'static str),
}

/// One call: its endpoint, query type, request body schema and the responses it reads, by status.
struct Call {
    endpoint: Endpoint,
    query: Option<fn() -> Shape>,
    request: Option<&'static str>,
    responses: &'static [(&'static str, Option<Body>)],
}

fn calls() -> Vec<Call> {
    vec![
        Call {
            endpoint: endpoints::AUTHORIZE_DEVICE,
            query: None,
            request: Some("DeviceAuthorizeRequest"),
            responses: &[("200", Some(Body::One("DeviceAuthorizationResponse")))],
        },
        Call {
            endpoint: endpoints::GET_DEVICE_TOKEN,
            query: None,
            request: Some("DeviceTokenRequest"),
            responses: &[
                ("200", Some(Body::One("DeviceTokenResponse"))),
                ("400", Some(Body::One("DeviceTokenError"))),
            ],
        },
        Call {
            endpoint: endpoints::SIGN_OUT_CURRENT_DEVICE,
            query: None,
            request: None,
            responses: &[("204", None)],
        },
        Call {
            endpoint: endpoints::GET_GAME_CATEGORIES,
            query: None,
            request: None,
            responses: &[("200", Some(Body::Array("GameCategoryResponse")))],
        },
        Call {
            endpoint: endpoints::GET_MY_PLAN,
            query: None,
            request: None,
            responses: &[("200", Some(Body::One("AccountPlanResponse")))],
        },
        Call {
            endpoint: endpoints::CREATE_VIDEO,
            query: Some(shape::<CreateVideoQuery>),
            request: None,
            responses: &[("200", Some(Body::One("CreateClipResponse")))],
        },
        Call {
            endpoint: endpoints::DELETE_CLIP,
            query: None,
            request: None,
            responses: &[("200", None)],
        },
        Call {
            endpoint: endpoints::PRESIGN_UPLOAD_PARTS,
            query: None,
            request: Some("PresignUploadPartsRequest"),
            responses: &[("200", Some(Body::One("PresignedUploadParts")))],
        },
        Call {
            endpoint: endpoints::LIST_UPLOAD_PARTS,
            query: Some(shape::<ListUploadPartsQuery>),
            request: None,
            responses: &[("200", Some(Body::One("UploadedParts")))],
        },
        Call {
            endpoint: endpoints::COMPLETE_UPLOAD,
            query: None,
            request: Some("CompleteUploadRequest"),
            responses: &[("200", Some(Body::One("CompletedUpload")))],
        },
        Call {
            endpoint: endpoints::GET_ME,
            query: None,
            request: None,
            responses: &[("200", Some(Body::One("CurrentUserResponse")))],
        },
        Call {
            endpoint: endpoints::GET_MY_FEATURES,
            query: None,
            request: None,
            responses: &[("200", Some(Body::One("FeatureFlagsResponse")))],
        },
        Call {
            endpoint: endpoints::GET_OPS_STORAGE,
            query: None,
            request: None,
            responses: &[("200", Some(Body::One("OpsStorageResponse")))],
        },
        Call {
            endpoint: endpoints::GET_OPS_TRANSCODING,
            query: None,
            request: None,
            responses: &[("200", Some(Body::One("OpsTranscodingResponse")))],
        },
        Call {
            endpoint: endpoints::GET_OPS_AI,
            query: None,
            request: None,
            responses: &[("200", Some(Body::One("OpsAiResponse")))],
        },
        Call {
            endpoint: endpoints::FIND_ADMIN_ACCOUNTS,
            query: Some(shape::<FindAdminAccountsQuery>),
            request: None,
            responses: &[("200", Some(Body::Array("AdminAccount")))],
        },
        Call {
            endpoint: endpoints::LIST_CONTENT_REPORTS,
            query: Some(shape::<ListContentReportsQuery>),
            request: None,
            responses: &[("200", Some(Body::Array("AdminContentReport")))],
        },
    ]
}

fn document() -> Value {
    let path = std::env::var_os("REELSHELF_OPENAPI")
        .map(PathBuf::from)
        .unwrap_or_else(|| {
            PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../frontend/src/api-client/Reelshelf.json")
        });
    let text = std::fs::read_to_string(&path).unwrap_or_else(|error| panic!("reading {}: {error}", path.display()));
    serde_json::from_str(&text).unwrap_or_else(|error| panic!("parsing {}: {error}", path.display()))
}

fn schema<'a>(document: &'a Value, name: &str) -> Option<&'a Value> {
    document.pointer(&format!("/components/schemas/{name}"))
}

fn ref_name(schema: &Value) -> Option<&str> {
    schema.get("$ref")?.as_str()?.strip_prefix("#/components/schemas/")
}

/// The schemas a property may hold besides null: itself, or each branch of a `oneOf`/`anyOf`.
fn branches(schema: &Value) -> Vec<&Value> {
    for key in ["oneOf", "anyOf"] {
        if let Some(Value::Array(options)) = schema.get(key) {
            return options.iter().filter(|option| !is_null(option)).collect();
        }
    }
    vec![schema]
}

fn is_null(schema: &Value) -> bool {
    schema.get("type") == Some(&json!("null"))
}

fn allows_null(schema: &Value) -> bool {
    match schema.get("type") {
        Some(Value::Array(types)) => types.contains(&json!("null")),
        Some(kind) => kind == "null",
        None => ["oneOf", "anyOf"].iter().any(|key| {
            schema
                .get(key)
                .and_then(Value::as_array)
                .is_some_and(|options| options.iter().any(is_null))
        }),
    }
}

fn has_type(schema: &Value, kind: &str) -> bool {
    match schema.get("type") {
        Some(Value::Array(types)) => types.contains(&json!(kind)),
        Some(other) => other == kind,
        None => false,
    }
}

/// The object schemas a property refers to, through `$ref`s, `oneOf`/`anyOf`, array items and map values.
fn referenced(schema: &Value, into: &mut BTreeSet<String>) {
    for branch in branches(schema) {
        if let Some(name) = ref_name(branch) {
            into.insert(name.to_owned());
        }
        if let Some(items) = branch.get("items") {
            referenced(items, into);
        }
        if let Some(values @ Value::Object(_)) = branch.get("additionalProperties") {
            referenced(values, into);
        }
    }
}

/// An example of the schema: with every property and no nulls (`full`), or only the required properties, null
/// wherever that is allowed.
fn example(document: &Value, schema: &Value, full: bool, depth: usize) -> Value {
    assert!(depth < 16, "schema nests too deep");
    if let Some(name) = ref_name(schema) {
        let target = self::schema(document, name).unwrap_or_else(|| panic!("no schema {name}"));
        return example(document, target, full, depth + 1);
    }
    if !full && allows_null(schema) {
        return Value::Null;
    }
    if let Some(branch) = branches(schema).first().copied()
        && !std::ptr::eq(branch, schema)
    {
        return example(document, branch, full, depth + 1);
    }
    if let Some(Value::Array(values)) = schema.get("enum") {
        return values.first().cloned().unwrap_or(Value::Null);
    }
    if has_type(schema, "string") {
        return match schema.get("format").and_then(Value::as_str) {
            Some("uuid") => json!("00000000-0000-0000-0000-000000000001"),
            Some("date-time") => json!("2026-01-01T00:00:00Z"),
            _ => json!("text"),
        };
    }
    if has_type(schema, "integer") {
        return json!(1);
    }
    if has_type(schema, "number") {
        return json!(1.5);
    }
    if has_type(schema, "boolean") {
        return json!(true);
    }
    if has_type(schema, "array") {
        let items = schema.get("items").cloned().unwrap_or(json!({}));
        return json!([example(document, &items, full, depth + 1)]);
    }
    if let Some(Value::Object(properties)) = schema.get("properties") {
        let required = required(schema);
        let mut object = Map::new();
        for (name, property) in properties {
            if full || required.contains(name.as_str()) {
                object.insert(name.clone(), example(document, property, full, depth + 1));
            }
        }
        return Value::Object(object);
    }
    if let Some(values @ Value::Object(_)) = schema.get("additionalProperties") {
        return json!({ "key": example(document, values, full, depth + 1) });
    }
    if has_type(schema, "object") {
        return json!({});
    }
    Value::Null
}

fn required(schema: &Value) -> BTreeSet<&str> {
    schema
        .get("required")
        .and_then(Value::as_array)
        .map(|names| names.iter().filter_map(Value::as_str).collect())
        .unwrap_or_default()
}

/// Every mismatch between the client and the document.
fn mismatches(document: &Value) -> Vec<String> {
    let mut problems = Vec::new();
    let types = types();
    let mapped: BTreeSet<&str> = types.iter().map(|map| map.schema).collect();

    for call in calls() {
        let endpoint = &call.endpoint;
        let method = endpoint.method.as_str().to_lowercase();
        let label = format!("{} {}", endpoint.method, endpoint.path);
        let Some(operation) = document.pointer(&format!(
            "/paths/{}/{method}",
            endpoint.path.replace('~', "~0").replace('/', "~1")
        )) else {
            problems.push(format!("{label}: not in the document"));
            continue;
        };
        if operation.get("operationId").and_then(Value::as_str) != Some(endpoint.operation_id) {
            problems.push(format!(
                "{label}: operationId is {:?}, not {:?}",
                operation.get("operationId"),
                endpoint.operation_id
            ));
        }

        let parameters = operation
            .get("parameters")
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default();
        let in_query = |parameter: &&Value| parameter.get("in") == Some(&json!("query"));
        let document_query: BTreeSet<&str> = parameters
            .iter()
            .filter(in_query)
            .filter_map(|parameter| parameter.get("name")?.as_str())
            .collect();
        let required_query: BTreeSet<&str> = parameters
            .iter()
            .filter(in_query)
            .filter(|parameter| parameter.get("required") == Some(&json!(true)))
            .filter_map(|parameter| parameter.get("name")?.as_str())
            .collect();
        let ours: BTreeSet<&str> = match call.query.map(|shape| shape()) {
            None => BTreeSet::new(),
            Some(Shape::Struct(fields)) => fields.iter().copied().collect(),
            Some(other) => {
                problems.push(format!("{label}: query type isn't a struct: {other}"));
                BTreeSet::new()
            }
        };
        for name in ours.difference(&document_query) {
            problems.push(format!("{label}: query parameter {name} isn't in the document"));
        }
        for name in required_query.difference(&ours) {
            problems.push(format!("{label}: required query parameter {name} isn't sent"));
        }
        for parameter in parameters.iter().filter(|p| p.get("in") == Some(&json!("path"))) {
            let name = parameter.get("name").and_then(Value::as_str).unwrap_or_default();
            if !endpoint.path.contains(&format!("{{{name}}}")) {
                problems.push(format!("{label}: path parameter {name} isn't in the path"));
            }
        }

        let request = operation
            .pointer("/requestBody/content/application~1json/schema")
            .and_then(ref_name);
        if request != call.request {
            problems.push(format!(
                "{label}: request body is {request:?}, the client sends {:?}",
                call.request
            ));
        }

        for (status, body) in call.responses {
            let Some(response) = operation.pointer(&format!("/responses/{status}")) else {
                problems.push(format!("{label}: no {status} response"));
                continue;
            };
            let schema = response.pointer("/content/application~1json/schema");
            let found = match schema {
                None => None,
                Some(schema) if has_type(schema, "array") => {
                    schema.get("items").and_then(ref_name).map(|name| format!("[{name}]"))
                }
                Some(schema) => Some(ref_name(schema).unwrap_or("an unnamed schema").to_owned()),
            };
            let expected = body.as_ref().map(|body| match body {
                Body::One(name) => name.to_string(),
                Body::Array(name) => format!("[{name}]"),
            });
            if found != expected {
                problems.push(format!(
                    "{label}: {status} answers {found:?}, the client reads {expected:?}"
                ));
            }
        }
    }

    for map in &types {
        let label = format!("{} ({})", map.rust, map.schema);
        let Some(schema) = schema(document, map.schema) else {
            problems.push(format!("{label}: schema not in the document"));
            continue;
        };
        match (map.shape)() {
            Shape::Struct(fields) => {
                let properties = schema.get("properties").and_then(Value::as_object);
                let names: BTreeSet<&str> = properties
                    .map(|properties| properties.keys().map(String::as_str).collect())
                    .unwrap_or_default();
                let fields: BTreeSet<&str> = fields.iter().copied().collect();
                for field in fields.difference(&names) {
                    problems.push(format!("{label}: field {field} isn't a property of the schema"));
                }
                for property in required(schema).difference(&fields) {
                    problems.push(format!("{label}: required property {property} isn't a field"));
                }
                let mut refs = BTreeSet::new();
                for property in properties.into_iter().flat_map(|properties| properties.values()) {
                    referenced(property, &mut refs);
                }
                for name in refs.iter().filter(|name| !mapped.contains(name.as_str())) {
                    problems.push(format!("{label}: refers to schema {name}, which no Rust type mirrors"));
                }
            }
            Shape::Enum(variants) => {
                let values: BTreeSet<&str> = schema
                    .get("enum")
                    .and_then(Value::as_array)
                    .map(|values| values.iter().filter_map(Value::as_str).collect())
                    .unwrap_or_default();
                // `Unknown` is the client's own catch-all for values newer than it.
                let variants: BTreeSet<&str> = variants
                    .iter()
                    .copied()
                    .filter(|variant| *variant != "unknown")
                    .collect();
                for variant in variants.difference(&values) {
                    problems.push(format!("{label}: variant {variant} isn't a value of the schema"));
                }
                for value in values.difference(&variants) {
                    problems.push(format!("{label}: value {value} has no variant"));
                }
            }
            Shape::Other(what) => problems.push(format!("{label}: not a struct or an enum: {what}")),
        }
        if let Some(decode) = map.decode {
            for full in [true, false] {
                let value = example(document, schema, full, 0);
                if let Err(error) = decode(&value) {
                    let which = if full {
                        "every property"
                    } else {
                        "only required properties"
                    };
                    problems.push(format!(
                        "{label}: an example with {which} doesn't deserialize: {error}: {value}"
                    ));
                }
            }
        }
    }

    problems
}

#[test]
fn client_matches_the_openapi_document() {
    let problems = mismatches(&document());
    assert!(
        problems.is_empty(),
        "the client and the OpenAPI document disagree:\n  {}",
        problems.join("\n  ")
    );
}

#[test]
fn every_endpoint_is_checked() {
    let checked: BTreeSet<&str> = calls().iter().map(|call| call.endpoint.operation_id).collect();
    let used: BTreeSet<&str> = endpoints::ALL.iter().map(|endpoint| endpoint.operation_id).collect();
    assert_eq!(checked, used);
}

#[test]
fn a_renamed_property_is_caught() {
    let mut document = document();
    let plan = document
        .pointer_mut("/components/schemas/AccountPlanResponse")
        .expect("AccountPlanResponse");
    let properties = plan["properties"].as_object_mut().unwrap();
    let max = properties.remove("max_clip_seconds").unwrap();
    properties.insert("max_clip_length_seconds".into(), max);
    for name in plan["required"].as_array_mut().unwrap() {
        if name == "max_clip_seconds" {
            *name = json!("max_clip_length_seconds");
        }
    }
    let problems = mismatches(&document);
    assert!(
        problems
            .iter()
            .any(|problem| problem.contains("field max_clip_seconds isn't a property")),
        "{problems:?}"
    );
    assert!(
        problems
            .iter()
            .any(|problem| problem.contains("required property max_clip_length_seconds isn't a field")),
        "{problems:?}"
    );
}
