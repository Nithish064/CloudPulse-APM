export interface TelemetrySpan {
  traceId: string;
  spanId: string;
  parentSpanId?: string | null;
  serviceName: string;
  operationName: string;
  startTime: string;
  durationMs: number;
  statusCode: number;
  hasError: boolean;
  errorMessage?: string | null;
  attributes: Record<string, string>;
}

export interface ServiceHealthSummary {
  serviceName: string;
  status: 'Healthy' | 'Degraded' | 'Critical';
  avgLatencyMs: number;
  p95LatencyMs: number;
  p99LatencyMs: number;
  errorRatePercent: number;
  totalRequests: number;
  activeAlertsCount: number;
  lastHeartbeat: string;
}

export interface AnomalyAlert {
  id: string;
  title: string;
  serviceName: string;
  metricName: string;
  severity: 0 | 1 | 2 | 3; // Info, Warning, High, Critical
  currentValue: number;
  baselineValue: number;
  zScore: number;
  description: string;
  suggestedRemediation: string;
  detectedAt: string;
  isAcknowledged: boolean;
}

export interface LogRecord {
  id: string;
  timestamp: string;
  level: string;
  message: string;
  serviceName: string;
  correlationId?: string;
  traceId?: string;
  spanId?: string;
  exception?: string;
}

export interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
  correlationId?: string;
  timestamp: string;
}
