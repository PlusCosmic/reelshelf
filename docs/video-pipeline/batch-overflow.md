# Overflow workers on Google Cloud Batch

Plan for moving the overflow pool (#248) from AWS to Google Cloud Batch. Spot `c4-highcpu-4` VMs in Paris
(`europe-west9`), started by the API when `transcode_run` backs up. Written 2026-10-22, after the pool's EC2
smoke test on AWS and two Batch start-up tests on GCP.

## Why move

The use case to design for: someone finishes a gaming session and uploads **2 to 10 clips at once**, and
wants them all playable as soon as possible. The pool should go wide quickly, one worker per clip, and be
gone again soon after the queue empties.

- **Parallelism is what matters.** A clip takes about 4 minutes to encode on 4 vCPU. Ten clips on the
  dedicated worker alone take about 40 minutes. With one worker per clip, the last clip is ready about 5
  minutes after scaling starts, whatever the platform.
- **AWS can't go wide.** The project's Fargate quota is 4 vCPU (one worker). A request for 16 vCPU has been
  open since 2026-10-19. EC2 Spot is capped at 8 vCPU (two workers). From zero, ECS managed scaling also
  launched two instances for a single task.
- **GCP gives more out of the box.** The project `reelshelf-512321` gets 32 vCPU across all regions by
  default, of which C4 in `europe-west9` may use 24 (6 workers), and Batch starts each worker on its own VM,
  which it deletes when the worker exits.
  Nothing sits idle between sessions.
- **Price.** Spot `c4-highcpu-4` in Paris costs about **$0.032 per worker-hour**, which is about half the
  price of Fargate Spot. A 10-clip session uses about 60 worker-minutes, about **3 cents**.

Considered and dropped: Cloud Run Jobs (about 5× the price per hour, no Spot), GKE Autopilot (a cluster to
run and upgrade for one job type, a 10 GiB ephemeral disk limit, 15 s Spot grace), and Batch in Finland
(Batch has no Stockholm location, and Finland has no C4 quota).

## What the tests showed (2026-10-22)

Both runs used four workers in one job, the worker image `sha-1d0b8397…` pulled from ghcr.io, and a dead
API address, so only start-up was measured.

| Region, machine                         | Submit → all 4 workers running | Notes                                                       |
| --------------------------------------- | ------------------------------ | ----------------------------------------------------------- |
| `europe-north1`, Spot `n4-highcpu-4`    | 54 s                           | VM boot ~35 s, image pull ~10 s                             |
| **`europe-west9`, Spot `c4-highcpu-4`** | **72 s**                       | scheduling and boot ~18 s slower; capacity on the first try |

On AWS for comparison, Fargate Spot took 40 s, and EC2 Spot took 2.5 min cold or 21 s on a warm instance.
The four VMs in each run came up within 0.5 s of each other.

### The smoke test against prod

One worker at a time, submitted by hand from the runbook in infrastructure's `gcp/overflow-workers/`, while
clips were uploaded:

- **Start-up:** 53 to 86 s from submit to the first claim. The worker authenticated with its own secret.
- **A 61 s 1080p h264 clip (307 MB):** download from R2 in 18 s (about 17 MB/s), encode and uploads in 3 min
  7 s, 37 MB of AV1 out. About 3× real time on 4 vCPU.
- **Idle exit:** the worker left at its `ExitWhenIdleAfter`, the job succeeded and Batch deleted the VM.
- **Network tier:** the VMs came up **Premium**, despite the project default being Standard: Batch writes
  `PREMIUM` into the instance template it builds from an instance policy. The jobs now name our own template
  (see [Egress](#egress)); a job submitted that way, as the scaler service account, got a Standard-tier VM.
- **Spot reclaim** (`simulate-maintenance-event` 30 s into a 1440p encode): Batch recorded the preemption as
  exit code 50001 and ran the task again on a new VM about 80 s later, which claimed the next clip. But the
  container was gone about 9 s into the notice with no SIGTERM handling and no `/release`, so the job it held
  stayed `running` until `Transcoding:StaleAfter` (then an hour). Hence the worker's preemption watcher and the
  shorter `StaleAfter` in [API changes](#api-changes-this-repo).

## Decisions

- **Batch, `europe-west9`, Spot `c4-highcpu-4`** (4 vCPU, 8 GB, Intel Xeon 6). The machine type, region and
  vCPU count are settings, so a 2-vCPU size or another region is a config change.
- **The API builds the job itself.** Batch has no equivalent of an ECS task definition, so `clips` holds the
  worker image, the machine shape and the worker's environment as `Overflow:Batch:*` settings. The worker
  image goes in `services/clips.yml` as `Overflow__Batch__WorkerImage`, so the existing bump PR updates it
  together with the dedicated worker's tag (it rewrites `reelshelf-worker:sha-` in every services file). A
  new worker image then needs no infrastructure deploy, unlike the AWS stack.
- **One Batch job per scale-up, with `taskCount` = workers wanted.** That's one API call per tick, and each
  task gets its own VM (`cpuMilli` 4000 on a 4-vCPU machine).
- **Its own credential.** The worker secret goes in Secret Manager as `reelshelf-worker-gcp`, and `clips`
  gets the same value as `Worker__Secrets__gcp`. Batch passes it to the container as `Worker__Secret`
  through `secretVariables`, so it never appears in the job spec.
- **Burst-friendly scaling.** See [Scaler settings](#scaler-settings). Scale up after about 30 s, not 2
  minutes; no cooldown; start every needed worker in one tick; workers exit after 60 s idle, not 3 minutes.
- **AWS stays until Batch has run real sessions.** `Overflow:Backend` switches between them, so going back
  is a settings change and a redeploy.

## GCP resources (infrastructure repo)

`gcp/overflow-workers/` in PlusCosmic/infrastructure, next to `aws/overflow-workers/`. It holds an idempotent
`setup.sh` (plain `gcloud`, so the repo stays free of Terraform and other tooling) and a `RUNBOOK.md` in the
style of the AWS one. Per instance (`prod`, later `dev`):

| Resource                                            | Details                                                                                                                                                                                                                           |
| --------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| APIs                                                | `batch`, `compute`, `logging`, `secretmanager`                                                                                                                                                                                    |
| VPC `reelshelf-overflow` + subnet in `europe-west9` | No ingress rules (GCP denies ingress by default), egress open. VMs get a public IP and no Cloud NAT (a NAT costs money while idle). Not the `default` VPC, whose rules open SSH, RDP and ICMP to the internet.                    |
| Instance template `reelshelf-overflow-c4-v1`        | Spot `c4-highcpu-4`, 60 GB Hyperdisk Balanced (C4 boots only from Hyperdisk), Batch's COS image, the subnet, the worker SA, and a **Standard**-tier IP. See [Egress](#egress). Templates can't be edited, so a change is a `-v2`. |
| Worker SA `reelshelf-batch-worker`                  | Already exists. `roles/batch.agentReporter`, `roles/logging.logWriter`, plus `roles/secretmanager.secretAccessor` on the one secret. No other access: the worker reaches nothing in GCP.                                          |
| Scaler SA `reelshelf-batch-scaler`                  | `roles/batch.jobsEditor` on the project, and `roles/iam.serviceAccountUser` on the worker SA only, so it can attach that SA to VMs. One JSON key, for `clips`.                                                                    |
| Secret `reelshelf-worker-gcp`                       | 48 random bytes, base64. The same value goes in `clips` as `Worker__Secrets__gcp`.                                                                                                                                                |
| (Later) Artifact Registry remote repo               | Proxies ghcr.io in `europe-west9`, to cut the ~10 s image pull. Optional.                                                                                                                                                         |

The test service account and the Batch API enable from 2026-10-22 are already in place. `setup.sh` adopts
them.

### Egress

Workers download sources from R2, which is free (ingress), and upload about 50 MB of outputs per clip, which
counts as internet egress. From Paris, Standard tier is **free for the first 200 GiB a month**, then
$0.085/GiB (`Network Standard Data Transfer Out to Internet from Paris` in the price list). Premium, GCP's
default, is $0.12/GiB from the first byte. 200 GiB is about 4,000 clips a month, so on Standard egress costs
nothing at the expected volume.

Batch can't be told the tier in a job's instance policy, and the project default doesn't help: the smoke test
(2026-10-22) found Batch writes `networkTier: PREMIUM` into the instance template it builds from the policy,
with the project default set to Standard. A job that names our own template (`instances[].instanceTemplate`)
gets the template's Standard-tier IP instead, so the jobs use one. The job must then name the same service
account as the template, and the template's Spot VMs need the `STOP` termination action (Batch's managed
instance groups refuse `DELETE`; Batch deletes the VMs itself). Standard routes over the public internet from
Paris rather than Google's backbone, which is fine for R2: Cloudflare has a point of presence there.

## API changes (this repo)

### Backend switch

`Overflow:Backend`: `ecs` (default, today's behaviour) or `batch`. The shared settings stay where they are
(`Enabled`, `Interval`, `JobTypes`, `ScaleUpAfter`, `MaxTasks`, `MaxStartPerTick`, `Cooldown`, `StartedBy`).
The ECS-only settings (`Cluster`, `TaskDefinition`, `CapacityProvider`, `Subnets`, `SecurityGroups`, `Aws:*`)
are only required when the backend is `ecs`. `OverflowOptions.Problems()` checks the settings of the chosen
backend, and `OverflowSetup` registers that backend's `IOverflowTaskLauncher`. `OverflowScaler` and the
backlog reader don't change, apart from the defaults below.

### `Overflow:Batch:*`

| Setting             | Example                                                        | Notes                                                                                                     |
| ------------------- | -------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `ProjectId`         | `reelshelf-512321`                                             |                                                                                                           |
| `Region`            | `europe-west9`                                                 | The job's location and `allowedLocations`.                                                                |
| `InstanceTemplate`  | `projects/…/global/instanceTemplates/reelshelf-overflow-c4-v1` | The machine type, Spot, boot disk, network and tier all come from it.                                     |
| `Vcpus`             | `4`                                                            | Sets `cpuMilli` and the worker's `Worker__LogicalProcessors`. Must match the template's machine.          |
| `MemoryMib`         | `6144`                                                         | The task's memory request. It must fit the machine (8 GB).                                                |
| `ServiceAccount`    | `reelshelf-batch-worker@…`                                     | The VMs' identity. Batch requires it to match the template's.                                             |
| `WorkerImage`       | `ghcr.io/pluscosmic/reelshelf-worker:sha-…`                    | Kept current by the bump PR.                                                                              |
| `WorkerSecret`      | `projects/…/secrets/reelshelf-worker-gcp/versions/latest`      |                                                                                                           |
| `ApiBaseUrl`        | `https://reelshelf.app`                                        |                                                                                                           |
| `ExitWhenIdleAfter` | `00:01:00`                                                     | Passed to the worker.                                                                                     |
| `MaxRunDuration`    | `12:00:00`                                                     | A safety cap only. A long backlog legitimately runs for hours (one Fargate worker took 7 h and 102 jobs). |
| `ProvisionTimeout`  | `00:10:00`                                                     | See [Jobs that never start](#jobs-that-never-start).                                                      |
| `CredentialsBase64` | (the scaler SA key, base64)                                    | Empty means Application Default Credentials. Base64 so the JSON survives an env var.                      |

### `BatchOverflowTaskLauncher`

The new `IOverflowTaskLauncher`, using `Google.Cloud.Batch.V1` (`BatchServiceClient`).

- **`StartTasksAsync(count)`**: one `CreateJob` with these settings:
  - **Job:** id `overflow-{yyyyMMdd-HHmmss}-{4 random}`; label `pool` = `StartedBy`; a `requestId` so the SDK's
    retries don't create the job twice.
  - **Task group:** `taskCount` = `parallelism` = `count`; a container runnable with `WorkerImage`; the
    environment `Worker__ApiBaseUrl`, `Worker__LogicalProcessors`, `Worker__ExitWhenIdleAfter` and
    `Worker__PreemptionNotice=gce`; `Worker__Secret` from `secretVariables`.
  - **Task spec:** `maxRunDuration`; `maxRetryCount` 2, with a lifecycle policy that retries on exit code
    50001 (Spot preemption). Batch also retries other exit codes up to `maxRetryCount`, which only restarts a
    worker that crashed: the jobs it held fail or go stale through the API as usual.
  - **Allocation policy:** `instances[].instanceTemplate`, the service account, and `allowedLocations` = the
    region. No instance policy: Batch's own templates get a Premium-tier IP.
  - **Logs policy:** `CLOUD_LOGGING`.

  It returns `count` once the job is created. Capacity problems appear later, as tasks that never start (see
  below).

- **`CountActiveTasksAsync()`**: `ListJobs` with the filter `labels.pool="<StartedBy>"` on jobs that aren't
  finished. For each job it adds up the tasks not yet `SUCCEEDED` or `FAILED`, using `taskCount` minus the
  finished counts in `status.taskGroups[].counts`, so tasks that were just created count straight away.
- **Pure helpers with unit tests**, as `BuildRunTaskRequest` has today: `BuildJob(options, count, now)`, and
  `ActiveTasks(job)` for the counting.

#### Jobs that never start

The first test hit `CPUS_PER_VM_FAMILY` quota 0 in Finland. The job sat in `SCHEDULED` with error events and
never started a VM. A stuck job would count against `MaxTasks` and block the pool indefinitely. So, while
counting, the launcher **deletes any pool job whose tasks have all been pending longer than
`ProvisionTimeout`**, and logs its latest status event (quota exceeded, no Spot capacity). The next tick then
counts that capacity as free again. Deleting a job that never started loses nothing: no worker claimed
anything.

### Preemption and stale jobs

The smoke test showed Batch stops a preempted worker's container without SIGTERM's grace, so the worker can't rely
on the shutdown path that ECS Spot draining gives it. Two changes:

- **The worker watches for preemption itself.** With `Worker:PreemptionNotice=gce`, `GcePreemptionWatcher` holds
  a hanging GET on the metadata server's `instance/preempted`. When it turns `TRUE` (the start of the 30 s
  notice), the job is stopped and released at once, whatever its stage, and the worker claims nothing more. It
  then waits for the VM to go rather than exiting, so Batch still sees exit code 50001 and retries the task.
- **`Transcoding:StaleAfter` is 5 minutes, not an hour.** Workers report at least every 30 s for the whole job
  (`HeartbeatInterval`, transfers included), so 5 minutes of silence means the worker is gone, however it went.
  That covers what the watcher can't: a VM gone before the notice, a crash, a lost network. The presigned URLs'
  lifetime used to be `StaleAfter` plus an hour, which would have shrunk with it, so it is its own setting now:
  `Transcoding:PresignLifetime`, 2 hours as before, never less than `StaleAfter` plus an hour.

### Scaler settings

The defaults were tuned for a long-running pool. For a session burst on Batch:

| Setting                    | Today | Batch        | Why                                                                                       |
| -------------------------- | ----- | ------------ | ----------------------------------------------------------------------------------------- |
| `ScaleUpAfter`             | 2 min | 30 s         | The dedicated worker takes the first clip at once; the rest shouldn't wait 2 minutes.     |
| `Cooldown`                 | 2 min | 0            | Batch counts tasks from the moment the job exists, so starting VMs aren't double-counted. |
| `MaxStartPerTick`          | 4     | = `MaxTasks` | Start the whole burst in one tick.                                                        |
| `MaxTasks`                 | 4     | 6            | C4's 24 vCPU in Paris / 4. Raise it when the quota goes up.                               |
| Worker `ExitWhenIdleAfter` | 3 min | 1 min        | Shrink soon after the queue empties. A late upload just triggers a new scale-up.          |

These are settings, not code changes, so ECS keeps its current values.

**`JobTypes` and the Bunny migration.** With a 30 s threshold, a migration backlog would also fill the pool.
That's fine while it's cheap, but set `Overflow:JobTypes=transcode,trim,thumbnails` if migrations should stay
on the dedicated worker.

### Tests and docs

- **Unit tests:** `BuildJob` (the shape, secret variables, the preemption-only retry policy, the id format),
  `ActiveTasks`, `Problems()` for each backend, and the stuck-job sweep against a fake client.
- **Docs:** the overflow sections of `docs/video-pipeline/workers-and-jobs.md` and `AGENTS.md` describe both
  backends. This plan moves into "Considered" once it's built.

## Rollout

1. **Infrastructure** (done, PlusCosmic/infrastructure#226 and #227): `gcp/overflow-workers/setup.sh` and
   `RUNBOOK.md`, run for `prod`; it prints the settings for Dokploy.
2. **Smoke test by hand** (done, see [above](#the-smoke-test-against-prod)): claim, transfers, encode, idle
   exit, the network tier and a simulated Spot preemption.
3. **API PR** (this repo): the backend switch, `BatchOverflowTaskLauncher`, options, the worker's preemption
   watcher, the shorter `StaleAfter`, tests and docs. Once its worker image is deployed, repeat the runbook's
   preemption test and check the job is released at once.
4. **Turn it on** (done 2026-10-22): `Overflow__Backend=batch`, the `Overflow__Batch__*` settings and the
   scaler settings above, with `MaxTasks=6`. A 50-clip stress test grew the pool one worker per waiting clip up
   to the cap; the job past the C4 quota (a test worker held 4 vCPU) failed with `QUOTA_EXCEEDED` and was
   deleted by the never-started sweep after 10 minutes. Dashboards: infrastructure's `services/monitoring.yml`.
5. **Quota**, when wanted: C4 in `europe-west9` (24 vCPU, 6 workers) binds first, then `CPUS_ALL_REGIONS` (32),
   then the region's 8 external IPs. For 16 workers ask for C4 in `europe-west9` 64, `CPUS_ALL_REGIONS` 64 and
   `IN_USE_ADDRESSES` 16. Not filed yet.
6. **AWS teardown**, once Batch has carried real sessions: the "Tear down" section of
   `aws/overflow-workers/README.md`. It's Harry's call.

## Open questions

- **Standard tier and upload speed.** The tier itself is settled (see [Egress](#egress)). Still to check: that
  uploads to R2 from a Standard-tier VM aren't noticeably slower than on Premium.
- **Service account keys.** Creating a key works on this personal project. If the project ever moves under an
  organization, `iam.disableServiceAccountKeyCreation` may block it.
- **Finished jobs pile up** in the Batch job list at no cost. A sweep deleting finished pool jobs older than a
  week could come later.
- **Dev:** a `dev` instance would need its own secret and settings on `clips-dev`, like the AWS pool's dev
  stack.
