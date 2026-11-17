# CloudPulse APM — Distributed Cloud Observability & Anomaly Engine

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![C# 14](https://img.shields.io/badge/C%23-14-239120?logo=csharp)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![Next.js 15](https://img.shields.io/badge/Next.js-15-000000?logo=next.dot.js)](https://nextjs.org/)
[![React 19](https://img.shields.io/badge/React-19-61DAFB?logo=react)](https://react.dev/)
[![Tailwind CSS](https://img.shields.io/badge/TailwindCSS-3.4-38B2AC?logo=tailwind-css)](https://tailwindcss.com/)
[![Serilog](https://img.shields.io/badge/Logging-Serilog-FF1493)](https://serilog.net/)

**CloudPulse APM** is an enterprise-grade Application Performance Monitoring (APM) and statistical anomaly detection platform designed for high-throughput distributed microservice systems.

It combines a high-performance **.NET 10 (C# 14)** microservice backend with an interactive **Next.js 15+ / React** observability dashboard, delivering real-time distributed trace waterfall diagrams, statistical latency anomaly detection (Z-score), error rate surge monitoring, and structured Serilog telemetry logging.

---

## 🏛️ Architecture Overview

```mermaid
flowchart TD
    subgraph Frontend ["Presentation Layer"]
        Dashboard["Next.js / React APM Dashboard (Port 3000 / 5000)\n- Real-time Trace Waterfall\n- Service Health Mesh\n- Anomaly Alerts & Root Cause Guidance\n- Synthetic Traffic Controller"]
    end

    subgraph GatewayLayer ["API Gateway & Reverse Proxy"]
        Gateway["Apm.Gateway (Port 5000)\n- Correlation ID Propagation (X-Correlation-ID)\n- Request Timing & Serilog Enriched Pipeline\n- SPA Host & Dynamic Downstream Routing"]
    end

    subgraph Microservices [".NET 10 Microservices"]
        Ingestion["Apm.IngestionService (Port 5001)\n- System.Threading.Channels Buffer\n- High-throughput Non-blocking Ingest (50k/sec)\n- Batch Flush Background Worker"]
        Anomaly["Apm.AnomalyEngineService (Port 5002)\n- Autonomous Background Detector\n- Z-Score & Moving Standard Deviation (σ)\n- Error Rate Surge Detection & AI Remediation"]
        Query["Apm.QueryService (Port 5003)\n- Trace Waterfall & Spans API\n- Service Mesh Telemetry & Health Analytics\n- Server-Sent Events (SSE) Live Feed\n- Synthetic Multi-Service Traffic Generator"]
    end

    subgraph StorageLayer ["Kernel & Shared State"]
        Shared["Apm.Shared\n- Serilog Unified Configuration\n- Global Exception Handling Middleware\n- CorrelationId Middleware\n- Request Logging Middleware\n- Concurrent In-Memory Telemetry Store"]
    end

    Dashboard <-->|HTTP / SSE Stream| Gateway
    Gateway -->|/api/ingest/*| Ingestion
    Gateway -->|/api/anomalies/*| Anomaly
    Gateway -->|/api/traces/*, /api/metrics/*, /api/live/*| Query

    Ingestion --> Shared
    Anomaly --> Shared
    Query --> Shared
```

---

## 📦 Project Structure

```text
CloudPulse-APM/
├── src/
│   ├── Apm.Shared/                      # Shared kernel: Domain models, middlewares, Serilog setup
│   │   ├── Models/                      # TelemetrySpan, MetricDataPoint, AnomalyAlert, etc.
│   │   ├── Middleware/                  # CorrelationId, GlobalExceptionHandling, RequestLogging
│   │   ├── Logging/                     # Unified Serilog builder extensions
│   │   └── Storage/                     # Thread-safe in-memory telemetry store
│   │
│   ├── Apm.Gateway/                     # Port 5000: API Gateway & Reverse Proxy
│   │   ├── Middleware/                  # GatewayReverseProxyMiddleware
│   │   ├── wwwroot/                     # Built-in interactive Single Page Application
│   │   └── Program.cs
│   │
│   ├── Apm.IngestionService/            # Port 5001: High-throughput ingestion microservice
│   │   ├── Services/                    # System.Threading.Channels buffer & batch workers
│   │   └── Program.cs                   # Endpoints: /api/ingest/spans, /api/ingest/metrics, /api/ingest/logs
│   │
│   ├── Apm.AnomalyEngineService/        # Port 5002: Statistical anomaly detection microservice
│   │   ├── Services/                    # Z-score calculator & hosted detector worker
│   │   └── Program.cs                   # Endpoints: /api/anomalies, /api/anomalies/evaluate
│   │
│   ├── Apm.QueryService/                # Port 5003: Analytical query & live stream microservice
│   │   ├── Services/                    # Synthetic traffic & multi-service span generator
│   │   └── Program.cs                   # Endpoints: /api/traces, /api/metrics, /api/live/stream
│   │
│   └── Apm.Dashboard/                   # Port 3000: Next.js 15+ App Router Dashboard
│       ├── app/                         # App Router layout, page, and globals.css
│       ├── lib/                         # TypeScript interfaces & API client definitions
│       └── package.json
│
├── .gitignore                           # Production gitignore for .NET, Next.js, and IDEs
├── build.ps1                            # One-click compile script for all microservices
├── start-all.ps1                        # Multi-service local orchestrator script
├── CloudPulseApm.sln                    # Visual Studio Solution
└── README.md
```

---

## 🚀 Key Architectural Features

### 1. High-Throughput Ingestion via `System.Threading.Channels`
`Apm.IngestionService` decouples HTTP request handling from persistence using bounded in-memory channels (`Channel<TelemetrySpan>`, `Channel<MetricDataPoint>`, `Channel<LogRecord>`). 
* HTTP requests return `202 Accepted` immediately upon writing to the channel.
* A hosted background service (`IngestionBatchWorker`) reads items in batches and flushes them to the data store every 250ms or when batch size exceeds 1,000 items.

### 2. Autonomous Statistical Anomaly Detection
`Apm.AnomalyEngineService` runs a continuous evaluation cycle every 5 seconds:
* **Latency Anomaly (Z-Score)**:
  $$\mu = \frac{1}{N}\sum_{i=1}^N x_i, \quad \sigma = \sqrt{\frac{1}{N}\sum_{i=1}^N (x_i - \mu)^2}, \quad Z = \frac{x_{\text{p95}} - \mu}{\sigma}$$
  When $Z \ge +2.0\sigma$, an incident is flagged with severity (*Warning*, *High*, or *Critical*), accompanied by root-cause remediation guidance.
* **Error Rate Surge**: Automatically flags incidents when error percentages exceed 5% or 15% thresholds.

### 3. Custom ASP.NET Core Middleware Pipeline
1. **`CorrelationIdMiddleware`**:
   * Extracts or generates `X-Correlation-ID`.
   * Enriches Serilog's `LogContext.PushProperty("CorrelationId", id)` for distributed tracing across services.
   * Attaches the header to outgoing responses and forwards it across downstream microservice HTTP calls.
2. **`GlobalExceptionHandlingMiddleware`**:
   * Encapsulates request pipelines in `try-catch`.
   * Catches `BadHttpRequestException` (400), `ArgumentException` (400), `KeyNotFoundException` (404), `TimeoutException` (504), and unhandled exceptions (500).
   * Emits structured Serilog error logs with correlation ID, client IP, route, and method.
   * Returns consistent RFC 7807 JSON error responses (`ApiResponse<T>.Fail(...)`).
3. **`RequestLoggingMiddleware`**:
   * Measures pipeline duration with high-resolution `Stopwatch`.
   * Emits a warning log (`[SLOW REQUEST DETECTED]`) if request execution exceeds 400ms.
4. **`GatewayReverseProxyMiddleware`**:
   * Dynamically forwards `/api/*` calls from the gateway to downstream microservices.
   * Safely handles downstream offline conditions, returning structured 502/504 fallbacks.

### 4. Serilog Structured Logging
* **Console Sink**: ANSI-colored output: `[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [Corr:{CorrelationId}] {Message}`.
* **Rolling File Sink**: Automatically writes to `logs/{ServiceName}-.log` with daily rolling and 7-day retention.

---

## 🛠️ Quick Start

### Prerequisites
* [.NET 10 SDK](https://dotnet.microsoft.com/)
* [Node.js 18+](https://nodejs.org/) (optional for standalone Next.js dashboard)
* PowerShell (Windows / macOS / Linux)

### Step 1: Build All Microservices
Run the automated build script:
```powershell
.\build.ps1
```

### Step 2: Launch All Microservices
Run the multi-service orchestrator script:
```powershell
.\start-all.ps1
```

This launches all microservices in parallel:
* 🌐 **API Gateway & Web Dashboard**: [http://localhost:5000](http://localhost:5000)
* 📥 **Ingestion Service**: [http://localhost:5001](http://localhost:5001)
* 🧠 **Anomaly Engine**: [http://localhost:5002](http://localhost:5002)
* 📊 **Query & Analytics Service**: [http://localhost:5003](http://localhost:5003)

### Step 3: Interactive Dashboard Controls
Open [http://localhost:5000](http://localhost:5000) in your browser:
1. **Microservice Mesh**: View status, P95 latency, error rates, and request volumes for `api-gateway`, `order-service`, `payment-gateway`, and `inventory-service`.
2. **Distributed Traces & Waterfall**: Select any trace to view the hierarchical span flamegraph with duration percentages and error attributes.
3. **Simulate Traffic**:
   * Click **🟢 Healthy Traffic** to generate nominal distributed transactions.
   * Click **🟡 Latency Spike (+800ms)** to trigger an anomalous latency spike.
   * Click **🔴 Error Cascade (5xx)** to simulate downstream gateway timeouts and failures.
4. **Anomaly Incidents**: View active Z-score anomalies ($+3.4\sigma$), root-cause analysis, and click **Acknowledge**.
5. **Structured Logs**: Inspect Serilog logs correlated by `TraceId` and `CorrelationId`.

---

### Step 4 (Optional): Run Standalone Next.js Dashboard
If you want to run the standalone Next.js App Router project:
```powershell
cd src/Apm.Dashboard
npm install
npm run dev
```
Open [http://localhost:3000](http://localhost:3000) (requests are automatically proxied to the Gateway at port 5000).

---

## 📄 License
This project is open-source and licensed under the [MIT License](LICENSE).
