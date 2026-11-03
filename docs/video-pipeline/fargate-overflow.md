# A second overflow pool on AWS Fargate Spot

Plan for running the overflow pool (#248) on two backends at once: ECS tasks on AWS Fargate Spot in Stockholm
(`eu-north-1`) next to Google Cloud Batch in Paris ([batch-overflow.md](batch-overflow.md)). Written 2026-11-03.
What is built is described in [workers-and-jobs.md](workers-and-jobs.md#the-overflow-pool-built-in-248).

## Why

- **Batch is at its quota.** C4 in `europe-west9` holds 24 vCPU, six 4-vCPU workers, and prod's
  `Overflow__MaxTasks=6` already fills it.
- **AWS has room.** The project's Fargate Spot quota is now 8 vCPU (it was 4 when the first AWS pool ran).
- **AWS is meant to become the main pool.** Using it now, every session, is what justifies asking for more
  quota. So Fargate goes first in the order and Batch takes the rest.

## Decisions

- **Several pools, filled in order** (2026-11-03). `Overflow:Pools` lists the backends, most preferred first;
  prod uses `ecs,batch`. Each pool has its own cap, its quota (`Overflow:Ecs:MaxTasks`, `Overflow:Batch:MaxTasks`),
  and `Overflow:MaxTasks` caps them together. Each tick works out how many workers to start as before, then gives
  them to the first pool up to its cap and the rest to the next. What a pool can't start (an error, or ECS
  reporting no Spot capacity) goes to the next pool in the same tick. A pool whose count fails is taken as full.
  The default `Pools` is `batch`, so a deployment that doesn't set it behaves exactly as before. Swapping the
  order, or dropping a pool, is a settings change.
- **Fargate Spot, not EC2 Spot** (2026-11-03). The quota increase is for Fargate Spot, and Fargate started
  workers in about 40 s in the first AWS run, faster than Batch (53 to 86 s) and much faster than EC2 Spot through
  ECS managed scaling (2.5 min cold, and two instances for one task). The EC2 capacity provider is not brought back.
- **Four 2-vCPU workers** (2026-11-03). The 8-vCPU quota is four 2-vCPU tasks or two 4-vCPU ones. Four is better
  for a burst of clips (one worker per clip) and exercises AWS more. A 2-vCPU worker encodes at roughly half the
  speed, about 8 minutes a clip rather than 4 (to be measured; see below). The task is 2 vCPU and 4 GiB (the
  smallest Fargate size for 2 vCPU), with `Worker__LogicalProcessors=2`. Once the quota grows, going back to 4 vCPU
  is a Terraform variable (`task_cpu`).
- **The API owns the worker image** (2026-11-03, "option B"). The first AWS pool had the image in the task
  definition, so each new worker needed a stack deploy. Now `Overflow__Ecs__WorkerImage` sits in
  `services/clips.yml`, the bump PR rewrites it with the other worker tags, and the API registers a revision of the
  task definition with that image when the latest one runs another. Everything else in the task definition (size,
  roles, secret, sandbox) stays Terraform's. This costs the API's user `ecs:RegisterTaskDefinition` and
  `ecs:DescribeTaskDefinition`, which IAM can't scope to one family, and `iam:PassRole` on the execution role only.
- **No preemption watcher.** Fargate Spot gives SIGTERM two minutes ahead; with `stopTimeout` 120 s the worker's
  usual shutdown (20 s grace, then release) hands the job back. `Worker__PreemptionNotice` stays empty.
- **Its own credential**, `Worker:Secrets:aws`, as in the first run: `reelshelf/worker/aws` in Secrets Manager,
  `ClipsAwsWorkerSecret` in Infisical.
- **Batch's `ProvisionTimeout` drops to 3 minutes** (2026-11-03). A Batch job counts its tasks as running from
  the moment it exists. When GCP can't give it VMs (quota, no Spot capacity), those tasks hold places in the
  overall count until the job is deleted, so no other pool is asked to cover them. Ten minutes was a long time to
  hold them; three is still well past Batch's normal 53 to 86 s start.

## AWS resources (infrastructure repo)

`aws/overflow-workers/`, Terraform in `eu-north-1` only, planned on pull requests and applied on merge to `main` by
`.github/workflows/aws-overflow-workers.yml` like `gcp/overflow-workers` (2026-11-03: the first pool's CloudFormation
template, deployed by hand, was ported so both clouds are managed the same way). `aws/bootstrap/`, applied by hand
once, holds the S3 state bucket and the two IAM users GitHub Actions signs in as by access key: plan (read-only, keys
in repository secrets) and apply (IAM limited to the `/reelshelf/` path, no secret values, keys in the `aws-apply`
environment, which only `main` can deploy to). They are users, not OIDC roles, because the project's SCP refuses
`iam:CreateOpenIDConnectProvider` (2026-11-03). `secrets.sh` makes the worker
secret's value and the scaler's access key outside the state. It holds the first pool's resources without the EC2
parts:

- a VPC with two public subnets, no NAT, and a security group with nothing in and only 443 out;
- the cluster `reelshelf-overflow-<instance>` with `FARGATE_SPOT`;
- the task definition family `reelshelf-worker-overflow-<instance>`: 2 vCPU, 4 GiB, 40 GiB ephemeral storage, the
  read-only root with `/work` and `/tmp` volumes and the `volume-init` container, every capability dropped,
  `stopTimeout` 120, `Worker__ApiBaseUrl`, `Worker__LogicalProcessors`, `Worker__ExitWhenIdleAfter=00:01:00` and
  `Worker__Secret` from Secrets Manager;
- a log group (14 days), the worker secret (no value), the execution role, and the API's IAM user.

Its RUNBOOK covers the bootstrap, the secrets, the settings and the smoke test.

## Rollout

1. **API PR** (this repo): pools, `EcsOverflowTaskLauncher`, settings, metrics tagged by pool, the bump PR's
   pattern, tests and docs.
2. **Infrastructure PR**: the Terraform, its bootstrap, workflow and RUNBOOK, the `Overflow__Ecs__*` settings and
   `Worker__Secrets__aws` in `services/clips.yml` with `Overflow__Pools` still `batch`, and the Grafana panels
   summing the pools.
3. **Bootstrap** `aws/bootstrap` by hand and put the CI users' keys in GitHub; merging the infrastructure PR then
   applies the pool. Run `secrets.sh prod` and put `ClipsAwsWorkerSecret` and the API's key in Infisical.
4. **Turn it on**: `Overflow__Pools=ecs,batch`, `Overflow__MaxTasks=10`, then upload a burst of clips and check
   that four Fargate tasks start first and Batch takes the rest.
5. **Measure** a 2-vCPU worker: encode time per clip, peak memory against the 4 GiB, and how often Fargate Spot
   has no capacity in `eu-north-1`. Then ask for more Fargate Spot quota with the numbers.

## Open questions

- Is 4 GiB enough for a 1080p60 encode at 2 vCPU? The Batch workers run with 6 GiB at 4 vCPU. If tasks are killed
  for memory, the next size is 2 vCPU and 6 GiB.
- When AWS becomes the main pool, does Batch stay as a spill-over, or go?
