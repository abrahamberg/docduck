# Indexer Execution Modes

The DocDuck indexer supports two execution modes to fit different deployment scenarios.

## Mode 1: Run-Once (Current Default) ✅

**Best for:** Kubernetes CronJobs, CI/CD pipelines, manual triggers

The indexer runs the full indexing job once and exits.

### How It Works
- Starts up
- Processes all enabled providers
- Indexes documents
- Updates database
- Exits with code 0 (success) or non-zero (failure)

### Configuration
**Current `Program.cs`** - Already configured this way

**Docker Compose:**
```yaml
indexer:
  restart: "no"  # Don't restart after exit
```

**Kubernetes CronJob:**
```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: docduck-indexer
spec:
  schedule: "0 */6 * * *"  # Every 6 hours
  jobTemplate:
    spec:
      template:
        spec:
          restartPolicy: Never
          containers:
            - name: indexer
              image: docduck-indexer:latest
```

### Triggering

**Manual (Docker Compose):**
```bash
docker compose run indexer
```

**Manual (Kubernetes):**
```bash
kubectl create job --from=cronjob/docduck-indexer manual-index-$(date +%s)
```

**Manual (Local):**
```bash
cd Indexer
dotnet run
```

### Advantages
✅ Clean exit after each run  
✅ Easy to monitor job completion  
✅ Works seamlessly with K8s CronJobs  
✅ No memory leaks from long-running processes  
✅ Simple restart on configuration changes  

### Disadvantages
❌ Requires external scheduler  
❌ Startup overhead for each run  

---

## Mode 2: Background Daemon 🔄

**Best for:** Single-server deployments, Docker Compose without orchestration, always-on indexing

The indexer runs as a long-lived background service with an internal scheduler.

### How It Works
- Starts up as a daemon
- Runs indexing on a schedule (configurable interval)
- Keeps running indefinitely
- Gracefully shuts down on SIGTERM

### Configuration

**1. Add to `appsettings.yaml`:**
```yaml
Scheduler:
  IntervalHours: 6  # Run every 6 hours
```

**2. Switch to daemon mode:**

**Option A - Use separate Program file:**
```bash
# Rename current to backup
mv Indexer/Program.cs Indexer/ProgramRunOnce.cs

# Use daemon version
mv Indexer/ProgramDaemon.cs Indexer/Program.cs
```

**Option B - Conditional compilation:**

Add to `Indexer.csproj`:
```xml
<PropertyGroup Condition="'$(Configuration)' == 'Daemon'">
  <DefineConstants>DAEMON_MODE</DefineConstants>
</PropertyGroup>
```

Then use preprocessor directives in `Program.cs`.

**3. Docker Compose for daemon:**
```yaml
indexer:
  restart: unless-stopped  # Keep running
  # Remove: restart: "no"
```

**4. Build and run:**
```bash
# Local
cd Indexer
dotnet run

# Docker
docker compose up -d indexer
```

### Advantages
✅ No external scheduler needed  
✅ Self-contained deployment  
✅ Simpler for single-server setups  
✅ Immediate startup, runs on schedule  

### Disadvantages
❌ Must manage long-running process health  
❌ Needs restart to pick up config changes  
❌ Potential memory leaks if not careful  
❌ Less cloud-native (CronJobs are better in K8s)  

---

## Comparison Table

| Feature | Run-Once | Daemon |
|---------|----------|--------|
| **Scheduling** | External (CronJob, cron, manual) | Internal (configurable interval) |
| **Kubernetes** | ✅ CronJob (recommended) | ⚠️ Deployment (works but not idiomatic) |
| **Docker Compose** | ⚠️ Needs external cron | ✅ Self-contained |
| **Restart Policy** | `Never` / `no` | `unless-stopped` / `Always` |
| **Health Checks** | Job completion | Liveness/readiness probes |
| **Config Changes** | Next run picks up | Requires restart |
| **Failure Handling** | Retry via scheduler | Continues to next scheduled run |
| **Resource Usage** | Low (only during run) | Constant (process always running) |

---

## Recommendations

### Use Run-Once (Current) If:
- ✅ Deploying to Kubernetes
- ✅ Using CI/CD pipelines
- ✅ Want cloud-native best practices
- ✅ Need clear job success/failure tracking
- ✅ Have orchestration infrastructure

### Use Daemon If:
- ✅ Simple Docker Compose deployment
- ✅ Single server with no orchestration
- ✅ Want self-contained service
- ✅ Prefer internal scheduling
- ✅ Have monitoring for long-lived processes

---

## Hybrid Approach: HTTP Trigger

Add an API endpoint to manually trigger indexing:

```csharp
// In Api/Program.cs
app.MapPost("/admin/trigger-indexing", async (
    IHttpClientFactory httpClientFactory,
    ILogger<Program> logger) =>
{
    // Call indexer container or trigger K8s Job
    // This allows manual triggering via API
});
```

This gives you:
- Scheduled runs (CronJob)
- On-demand runs (API call)
- Best of both worlds

---

## Current Setup Status

**Active Mode:** ✅ Run-Once  
**Scheduler:** Kubernetes CronJob (every 6 hours)  
**Docker Compose:** Configured for manual runs (`restart: "no"`)

**To Switch to Daemon:**
1. Use `ProgramDaemon.cs` as your `Program.cs`
2. Add `Scheduler:IntervalHours` to `appsettings.yaml`
3. Change `restart: "no"` to `restart: unless-stopped` in docker-compose

The current run-once mode is the recommended approach for your deployment.
