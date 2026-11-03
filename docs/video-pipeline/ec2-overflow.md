# A third overflow pool on AWS EC2 Spot

Plan for adding ECS tasks on EC2 Spot instances in Stockholm (`eu-north-1`) as the overflow pool's (#248) first
choice, ahead of Fargate Spot ([fargate-overflow.md](fargate-overflow.md)) and Google Cloud Batch
([batch-overflow.md](batch-overflow.md)). Written 2026-11-03. What is built is described in
[workers-and-jobs.md](workers-and-jobs.md#aws-ecs-ec2-spot-and-fargate-spot).

## Why

- **Fargate Spot costs about three times as much per clip.** On prod's 2-vCPU Fargate tasks a 1440p60 clip takes
  about 10.6 times its length to encode; on a 4-vCPU EC2 Spot instance about 3.6 times. Per clip, EC2 Spot came out
  at roughly a third of Fargate Spot's cost.
- **The quota is separate.** EC2 Spot instances count against the account's Spot vCPU quota, not Fargate's, so the
  new pool adds workers rather than taking Fargate's.

## Decisions

- **EC2 Spot goes first** (2026-11-03). Prod becomes `Overflow__Pools=ec2,ecs,batch`: EC2 up to its cap, then
  Fargate, then Batch. Anything EC2 can't start (an error, no Spot capacity) goes to Fargate in the same tick.
- **One launcher for both AWS pools** (2026-11-03). `EcsOverflowTaskLauncher` runs both: the pool `ec2` takes its
  settings from `Overflow:Ec2:*`, `ecs` from `Overflow:Ecs:*`, each with its own client and cap. The capacity
  provider is a setting (`CapacityProvider`), `FARGATE_SPOT` by default for `ecs` so prod's settings keep working,
  and required for `ec2`.
- **Its own cluster** (2026-11-03), e.g. `reelshelf-overflow-ec2-prod`, with an Auto Scaling group capacity provider
  (Spot, managed scaling, down to zero). Tasks are counted by cluster and `startedBy`, and both pools share
  `Overflow:StartedBy`, so on one cluster each pool would count the other's tasks. Startup fails when the two
  pools name the same cluster.
- **`host` network mode** (2026-11-03). The subnets are public with no NAT, as for Fargate, but EC2 can't give an
  `awsvpc` task a public IP. In `host` mode the task uses its instance's public IP, so it reaches the API and R2
  without a NAT. The API reads the network mode from the task definition it describes or registers, not from a
  setting: only an `awsvpc` task gets a network configuration (subnets, security groups, and a public IP on
  Fargate). So `Overflow:Ec2:Subnets` and `SecurityGroups` are optional, needed only if the task definition ever
  goes back to `awsvpc` (which would then need a NAT).
- **One worker per 4-vCPU instance**, `Worker__LogicalProcessors=4`, the same image, secret
  (`Worker:Secrets:aws`) and sandbox as Fargate. The API swaps the image in the EC2 family as it does in the Fargate
  one; an EC2 task definition may leave the task-level CPU and memory to the container, and the copy keeps its
  placement constraints.
- **Provisioning tasks count** (2026-11-03). A task waits in `PROVISIONING` while managed scaling boots an instance
  (1 to 3 minutes). Its desired status is already `RUNNING`, so `ListTasks` counts it from the start and the next
  tick doesn't start another for the same job.
- **No worker changes, no preemption watcher** (2026-11-03). The ECS agent runs with
  `ECS_ENABLE_SPOT_INSTANCE_DRAINING=true`: on the two-minute interruption notice it drains the instance and stops
  the task with SIGTERM and the task definition's `stopTimeout`, as Fargate Spot does. The worker's usual shutdown
  (20 s grace, then release) hands the job back. `Worker__PreemptionNotice` stays empty; the GCE watcher is for
  Batch, whose Spot VMs give no such grace. The worker opens no port, so `host` mode needs nothing of it either.
- **Considered:** the first AWS pool ran on EC2 Spot through ECS managed scaling before Batch, and was dropped for
  starting slowly (2.5 min cold) and once starting two instances for one task. With Fargate and Batch behind it,
  a slow start now costs a little latency on the first clips of a burst, not throughput, and the price difference
  is worth it.

## AWS resources (infrastructure repo)

In `aws/overflow-workers/` next to the Fargate pool, built in the infrastructure repo:

- the cluster `reelshelf-overflow-ec2-<instance>` with an Auto Scaling group capacity provider: Spot, 4-vCPU
  instance types, the ECS-optimised AMI with `ECS_ENABLE_SPOT_INSTANCE_DRAINING=true`, managed scaling, minimum 0, in the existing public subnets with a public IP and a security group with
  nothing in and only 443 out;
- the task definition family `reelshelf-worker-overflow-ec2-<instance>`: `requiresCompatibilities = ["EC2"]`,
  `networkMode = "host"`, sized so one task fills one instance, `stopTimeout` 120, the Fargate family's image,
  volumes, sandbox and `Worker__Secret`, with `Worker__LogicalProcessors=4`;
- for the scaler's IAM user, on the new cluster and family: `ecs:RunTask`, `ecs:ListTasks`,
  `ecs:DescribeTaskDefinition`, `ecs:RegisterTaskDefinition`, and `iam:PassRole` on the execution role (and the
  task role, if the family has one).

## Settings

`Overflow:Ec2:*`, the same keys as `Overflow:Ecs:*`:

| Setting                           | Required | What it is                                                        |
| --------------------------------- | -------- | ----------------------------------------------------------------- |
| `Overflow__Ec2__Region`           | yes      | `eu-north-1`                                                      |
| `Overflow__Ec2__Cluster`          | yes      | the EC2 cluster, not the Fargate one                              |
| `Overflow__Ec2__CapacityProvider` | yes      | the cluster's Auto Scaling group capacity provider; no default    |
| `Overflow__Ec2__TaskDefinition`   | yes      | the family, without a revision                                    |
| `Overflow__Ec2__MaxTasks`         | no       | the Spot vCPU quota over 4; 0 means `Overflow__MaxTasks`          |
| `Overflow__Ec2__WorkerImage`      | no       | kept current by the bump PR; empty runs the latest revision as is |
| `Overflow__Ec2__AccessKeyId`      | no       | the scaler user's key, with `SecretAccessKey` (both or neither)   |
| `Overflow__Ec2__SecretAccessKey`  | no       |                                                                   |
| `Overflow__Ec2__Subnets`          | no       | only for an `awsvpc` task definition                              |
| `Overflow__Ec2__SecurityGroups`   | no       | only for an `awsvpc` task definition                              |
| `Overflow__Ecs__CapacityProvider` | no       | the Fargate pool's, `FARGATE_SPOT` by default                     |

The bump PR rewrites `Overflow__Ec2__WorkerImage=` along with the other worker tags.

## Rollout

1. **API PR** (this repo): the `ec2` pool, the capacity provider and network mode in `EcsOverflowTaskLauncher`,
   settings, the bump PR's pattern, tests and docs. Nothing changes until `Overflow__Pools` names `ec2`.
2. **Infrastructure PR**: the cluster, capacity provider, task definition and IAM above, and the `Overflow__Ec2__*`
   settings in `services/clips.yml` with `Overflow__Pools` still `ecs,batch`.
3. **Turn it on**: `Overflow__Pools=ec2,ecs,batch`, `Overflow__Ec2__MaxTasks` from the quota, and raise
   `Overflow__MaxTasks` to cover all three. Upload a burst of clips and check that EC2 tasks start first, sit in
   `PROVISIONING` while their instances boot without more being started for the same jobs, and that Fargate and
   Batch take the rest.
4. **Measure** the cost per clip and the start time from `RunTask` to the first claim, and whether managed scaling
   ever starts more instances than tasks.

## Open questions

- Is a 1 to 3 minute cold start worth keeping `ScaleUpAfter` at 30 s, or should EC2 keep a warm instance during
  sessions?
- Once EC2 has enough quota, do Fargate and Batch stay as spill-over, or go?
