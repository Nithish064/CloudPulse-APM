'use client';

import { useState, useEffect } from 'react';
import { ServiceHealthSummary, TelemetrySpan, AnomalyAlert, LogRecord } from '@/lib/types';

export default function DashboardPage() {
  const [services, setServices] = useState<ServiceHealthSummary[]>([]);
  const [traces, setTraces] = useState<TelemetrySpan[]>([]);
  const [selectedTrace, setSelectedTrace] = useState<{ traceId: string; spans: TelemetrySpan[] } | null>(null);
  const [alerts, setAlerts] = useState<AnomalyAlert[]>([]);
  const [logs, setLogs] = useState<LogRecord[]>([]);
  const [activeTab, setActiveTab] = useState<'overview' | 'traces' | 'alerts' | 'logs'>('overview');
  const [simulating, setSimulating] = useState(false);
  const [filterError, setFilterError] = useState(false);
  const [sseConnected, setSseConnected] = useState(false);

  const refreshData = async () => {
    try {
      const [svcRes, traceRes, alertRes, logRes] = await Promise.all([
        fetch('/api/metrics/services').then(r => r.json()),
        fetch('/api/traces?limit=30').then(r => r.json()),
        fetch('/api/anomalies').then(r => r.json()),
        fetch('/api/logs?limit=40').then(r => r.json())
      ]);

      if (svcRes.success && svcRes.data) setServices(svcRes.data);
      if (traceRes.success && traceRes.data) {
        setTraces(traceRes.data);
        if (!selectedTrace && traceRes.data.length > 0) {
          fetchTraceDetail(traceRes.data[0].traceId);
        }
      }
      if (alertRes.success && alertRes.data) setAlerts(alertRes.data);
      if (logRes.success && logRes.data) setLogs(logRes.data);
    } catch (err) {
      console.error("Failed to load initial data", err);
    }
  };

  const fetchTraceDetail = async (traceId: string) => {
    try {
      const res = await fetch(`/api/traces/${traceId}`).then(r => r.json());
      if (res.success && res.data) {
        setSelectedTrace({ traceId, spans: res.data });
      }
    } catch (err) {
      console.error("Failed to fetch trace", err);
    }
  };

  const simulateTraffic = async (scenario: string) => {
    setSimulating(true);
    try {
      await fetch(`/api/simulator/generate?scenario=${scenario}`, { method: 'POST' });
      await fetch('/api/anomalies/evaluate', { method: 'POST' });
      await refreshData();
    } catch (err) {
      console.error("Simulation error", err);
    } finally {
      setSimulating(false);
    }
  };

  const acknowledgeAlert = async (id: string) => {
    try {
      await fetch(`/api/anomalies/${id}/acknowledge`, { method: 'POST' });
      setAlerts(prev => prev.map(a => a.id === id ? { ...a, isAcknowledged: true } : a));
    } catch (err) {
      console.error("Failed to acknowledge", err);
    }
  };

  useEffect(() => {
    refreshData();

    let eventSource: EventSource | null = null;
    try {
      eventSource = new EventSource('/api/live/stream');
      eventSource.onopen = () => setSseConnected(true);
      eventSource.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          if (data.services) setServices(data.services);
          if (data.alerts) setAlerts(prev => [...data.alerts, ...prev.filter(p => !data.alerts.some((d: AnomalyAlert) => d.id === p.id))]);
          if (data.spans) setTraces(prev => {
            const combined = [...data.spans, ...prev];
            const seen = new Set();
            return combined.filter(item => {
              const k = item.traceId + item.spanId;
              return seen.has(k) ? false : seen.add(k);
            }).slice(0, 50);
          });
        } catch (e) {
          console.error("SSE parse error", e);
        }
      };
      eventSource.onerror = () => {
        setSseConnected(false);
      };
    } catch (e) {
      console.warn("SSE not available", e);
    }

    return () => {
      if (eventSource) eventSource.close();
    };
  }, []);

  return (
    <div className="flex flex-col min-h-screen">
      {/* Top Navigation Bar */}
      <header className="border-b border-gray-800 bg-[#111827]/80 backdrop-blur sticky top-0 z-50 px-6 py-3.5">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          <div className="flex items-center space-x-4">
            <div className="flex items-center space-x-2">
              <div className="w-8 h-8 rounded-lg bg-emerald-500 flex items-center justify-center font-bold text-gray-950 text-lg shadow-lg shadow-emerald-500/30">
                ⚡
              </div>
              <div>
                <h1 className="text-xl font-bold tracking-tight bg-gradient-to-r from-emerald-400 to-cyan-400 bg-clip-text text-transparent">
                  CloudPulse APM (Next.js 15)
                </h1>
                <span className="text-xs text-gray-400">Distributed Observability & Anomaly Engine</span>
              </div>
            </div>

            <div className="hidden md:flex items-center space-x-1.5 ml-6 bg-gray-900 border border-gray-800 rounded-full px-3 py-1 text-xs">
              <span className={`w-2 h-2 rounded-full ${sseConnected ? 'bg-emerald-400 animate-pulse' : 'bg-amber-400'}`}></span>
              <span className="text-gray-300">{sseConnected ? 'SSE Live Feed Connected' : 'Polling Mode'}</span>
            </div>
          </div>

          {/* Traffic Simulator Controls */}
          <div className="flex items-center space-x-2">
            <span className="text-xs text-gray-400 mr-1 hidden sm:inline">Simulate Traffic:</span>
            <button
              disabled={simulating}
              onClick={() => simulateTraffic('normal')}
              className="px-2.5 py-1.5 text-xs font-medium rounded-md bg-gray-800 hover:bg-gray-700 text-gray-200 border border-gray-700 transition"
            >
              🟢 Healthy
            </button>
            <button
              disabled={simulating}
              onClick={() => simulateTraffic('latency_spike')}
              className="px-2.5 py-1.5 text-xs font-medium rounded-md bg-amber-950/70 hover:bg-amber-900 text-amber-300 border border-amber-800/80 transition"
            >
              🟡 Latency Spike (+800ms)
            </button>
            <button
              disabled={simulating}
              onClick={() => simulateTraffic('error_cascade')}
              className="px-2.5 py-1.5 text-xs font-medium rounded-md bg-rose-950/70 hover:bg-rose-900 text-rose-300 border border-rose-800/80 transition"
            >
              🔴 Error Cascade (5xx)
            </button>
          </div>
        </div>
      </header>

      {/* Navigation Tabs */}
      <div className="border-b border-gray-800 bg-[#0e1422] px-6">
        <div className="max-w-7xl mx-auto flex space-x-8 text-sm font-medium">
          <button
            onClick={() => setActiveTab('overview')}
            className={`py-3 border-b-2 flex items-center space-x-2 transition ${
              activeTab === 'overview'
                ? 'border-emerald-500 text-emerald-400'
                : 'border-transparent text-gray-400 hover:text-gray-200'
            }`}
          >
            <span>📊 Microservice Mesh</span>
            <span className="text-xs bg-gray-800 px-2 py-0.5 rounded-full">{services.length}</span>
          </button>
          <button
            onClick={() => setActiveTab('traces')}
            className={`py-3 border-b-2 flex items-center space-x-2 transition ${
              activeTab === 'traces'
                ? 'border-emerald-500 text-emerald-400'
                : 'border-transparent text-gray-400 hover:text-gray-200'
            }`}
          >
            <span>⚡ Distributed Traces & Waterfall</span>
            <span className="text-xs bg-gray-800 px-2 py-0.5 rounded-full">{traces.length}</span>
          </button>
          <button
            onClick={() => setActiveTab('alerts')}
            className={`py-3 border-b-2 flex items-center space-x-2 transition ${
              activeTab === 'alerts'
                ? 'border-emerald-500 text-emerald-400'
                : 'border-transparent text-gray-400 hover:text-gray-200'
            }`}
          >
            <span>🚨 Anomaly Incidents</span>
            {alerts.filter(a => !a.isAcknowledged).length > 0 && (
              <span className="text-xs bg-rose-600 text-white font-bold px-2 py-0.5 rounded-full animate-pulse">
                {alerts.filter(a => !a.isAcknowledged).length}
              </span>
            )}
          </button>
          <button
            onClick={() => setActiveTab('logs')}
            className={`py-3 border-b-2 flex items-center space-x-2 transition ${
              activeTab === 'logs'
                ? 'border-emerald-500 text-emerald-400'
                : 'border-transparent text-gray-400 hover:text-gray-200'
            }`}
          >
            <span>📜 Structured Logs</span>
          </button>
        </div>
      </div>

      {/* Main Content */}
      <main className="flex-1 max-w-7xl mx-auto w-full p-6 space-y-6">
        {/* OVERVIEW TAB */}
        {activeTab === 'overview' && (
          <div className="space-y-6">
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              {services.map((svc) => (
                <div
                  key={svc.serviceName}
                  className="bg-[#111827] border border-gray-800 rounded-xl p-4 flex flex-col justify-between hover:border-gray-700 transition"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-bold text-gray-100">{svc.serviceName}</span>
                    <span
                      className={`text-xs px-2 py-0.5 rounded-full font-semibold ${
                        svc.status === 'Healthy'
                          ? 'bg-emerald-950 text-emerald-400 border border-emerald-800'
                          : svc.status === 'Degraded'
                          ? 'bg-amber-950 text-amber-400 border border-amber-800'
                          : 'bg-rose-950 text-rose-400 border border-rose-800'
                      }`}
                    >
                      {svc.status}
                    </span>
                  </div>

                  <div className="grid grid-cols-2 gap-2 my-4 pt-2 border-t border-gray-800/60 text-xs">
                    <div>
                      <span className="text-gray-400">P95 Latency</span>
                      <div className="text-base font-mono font-bold text-gray-200 mt-0.5">{svc.p95LatencyMs} ms</div>
                    </div>
                    <div>
                      <span className="text-gray-400">Error Rate</span>
                      <div className={`text-base font-mono font-bold mt-0.5 ${svc.errorRatePercent > 5 ? 'text-rose-400' : 'text-gray-200'}`}>
                        {svc.errorRatePercent}%
                      </div>
                    </div>
                  </div>

                  <div className="text-[10px] text-gray-500 pt-2 border-t border-gray-800/50 flex items-center justify-between">
                    <span>Heartbeat: {new Date(svc.lastHeartbeat).toLocaleTimeString()}</span>
                    <span className="text-emerald-400 font-mono">200 OK</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* TRACES TAB */}
        {activeTab === 'traces' && (
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
            <div className="lg:col-span-5 space-y-3">
              <div className="flex items-center justify-between">
                <h3 className="text-sm font-semibold text-gray-300">Distributed Traces</h3>
                <label className="flex items-center space-x-1.5 text-xs text-gray-400 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={filterError}
                    onChange={(e) => setFilterError(e.target.checked)}
                    className="rounded border-gray-700 bg-gray-900 text-rose-500 focus:ring-0"
                  />
                  <span>Errors Only</span>
                </label>
              </div>

              <div className="bg-[#111827] border border-gray-800 rounded-xl overflow-hidden divide-y divide-gray-800 max-h-[600px] overflow-y-auto">
                {traces
                  .filter(t => !filterError || t.hasError)
                  .map((trace) => (
                    <div
                      key={trace.spanId}
                      onClick={() => fetchTraceDetail(trace.traceId)}
                      className={`p-3 cursor-pointer hover:bg-gray-800/50 transition flex items-center justify-between text-xs ${
                        selectedTrace?.traceId === trace.traceId ? 'bg-gray-800/80 border-l-4 border-emerald-500' : ''
                      }`}
                    >
                      <div>
                        <div className="flex items-center space-x-2">
                          <span className={`w-2 h-2 rounded-full ${trace.hasError ? 'bg-rose-500' : 'bg-emerald-500'}`}></span>
                          <span className="font-semibold text-gray-200">{trace.serviceName}</span>
                          <span className="text-gray-400 font-mono">[{trace.statusCode}]</span>
                        </div>
                        <div className="text-[11px] text-gray-400 font-mono mt-1 truncate max-w-[240px]">
                          {trace.operationName}
                        </div>
                      </div>
                      <div className="text-right">
                        <span className="font-mono font-bold text-gray-300">{trace.durationMs.toFixed(1)} ms</span>
                        <div className="text-[10px] text-gray-500 mt-0.5">
                          {new Date(trace.startTime).toLocaleTimeString()}
                        </div>
                      </div>
                    </div>
                  ))}
              </div>
            </div>

            {/* Waterfall Viewer */}
            <div className="lg:col-span-7 bg-[#111827] border border-gray-800 rounded-xl p-5">
              {selectedTrace ? (
                <div className="space-y-4">
                  <div className="flex items-center justify-between pb-3 border-b border-gray-800">
                    <div>
                      <h4 className="text-sm font-semibold text-gray-200">Trace Waterfall</h4>
                      <span className="text-xs text-gray-400 font-mono">ID: {selectedTrace.traceId}</span>
                    </div>
                    <span className="text-xs bg-gray-800 text-gray-300 px-2.5 py-1 rounded font-mono">
                      {selectedTrace.spans.length} Spans
                    </span>
                  </div>

                  <div className="space-y-3 pt-2">
                    {(() => {
                      const totalDuration = Math.max(...selectedTrace.spans.map(s => s.durationMs), 1);
                      return selectedTrace.spans.map((span) => {
                        const widthPercent = Math.max((span.durationMs / totalDuration) * 100, 8);
                        const isChild = !!span.parentSpanId;

                        return (
                          <div key={span.spanId} className="space-y-1">
                            <div className="flex items-center justify-between text-xs">
                              <div className={`flex items-center space-x-2 ${isChild ? 'pl-4' : ''}`}>
                                <span className="font-semibold text-gray-200">{span.serviceName}</span>
                                <span className="text-gray-400 text-[11px] truncate max-w-[280px]">
                                  {span.operationName}
                                </span>
                              </div>
                              <div className="flex items-center space-x-2 font-mono text-[11px]">
                                <span className={span.hasError ? 'text-rose-400 font-bold' : 'text-gray-300'}>
                                  {span.durationMs.toFixed(1)} ms
                                </span>
                                <span className="px-1.5 py-0.5 rounded text-[10px] bg-gray-800 text-gray-300">
                                  {span.statusCode}
                                </span>
                              </div>
                            </div>

                            <div className="w-full bg-gray-900 rounded-full h-3 overflow-hidden">
                              <div
                                className={`h-full rounded-full transition-all duration-500 ${
                                  span.hasError ? 'bg-rose-500' : span.durationMs > 300 ? 'bg-amber-500' : 'bg-emerald-500'
                                }`}
                                style={{ width: `${widthPercent}%` }}
                              ></div>
                            </div>
                          </div>
                        );
                      });
                    })()}
                  </div>
                </div>
              ) : (
                <div className="h-64 flex items-center justify-center text-gray-500 text-sm">
                  Select a trace on the left to view timeline
                </div>
              )}
            </div>
          </div>
        )}

        {/* ALERTS TAB */}
        {activeTab === 'alerts' && (
          <div className="space-y-4">
            <h3 className="text-base font-semibold text-gray-200">Active Anomaly Incidents</h3>
            <div className="grid grid-cols-1 gap-4">
              {alerts.map((alert) => (
                <div
                  key={alert.id}
                  className={`p-5 rounded-xl border transition ${
                    alert.isAcknowledged ? 'bg-[#111827]/40 border-gray-800 opacity-60' : 'bg-rose-950/20 border-rose-800'
                  }`}
                >
                  <div className="flex items-start justify-between">
                    <div>
                      <div className="flex items-center space-x-2">
                        <h4 className="text-sm font-bold text-gray-100">{alert.title}</h4>
                        <span className="text-xs font-mono font-bold text-amber-400">
                          Z-Score: +{alert.zScore}&sigma;
                        </span>
                      </div>
                      <p className="text-xs text-gray-300 mt-1.5">{alert.description}</p>
                      <div className="mt-3 p-2.5 rounded bg-gray-900/80 border border-gray-800 text-xs text-gray-300">
                        <span className="text-emerald-400 font-bold">💡 Suggested Remediation: </span>
                        {alert.suggestedRemediation}
                      </div>
                    </div>

                    {!alert.isAcknowledged && (
                      <button
                        onClick={() => acknowledgeAlert(alert.id)}
                        className="px-3 py-1 bg-emerald-600 hover:bg-emerald-500 text-white rounded text-xs font-medium"
                      >
                        Acknowledge
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* LOGS TAB */}
        {activeTab === 'logs' && (
          <div className="bg-[#111827] border border-gray-800 rounded-xl p-5 space-y-3 font-mono text-xs divide-y divide-gray-800">
            {logs.map((log) => (
              <div key={log.id} className="py-2.5 flex items-start space-x-3">
                <span className="text-gray-500">{new Date(log.timestamp).toLocaleTimeString()}</span>
                <span className="text-gray-300 font-bold">[{log.level}]</span>
                <span className="text-emerald-400">[{log.serviceName}]</span>
                <div className="flex-1 text-gray-200">{log.message}</div>
              </div>
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
