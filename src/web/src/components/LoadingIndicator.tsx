import React from 'react';

export const LoadingIndicator: React.FC<{ label?: string }> = ({ label = 'Thinking' }) => {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
      <div className="spinner" style={{ width: 16, height: 16 }}>
        <svg viewBox="0 0 50 50">
          <circle cx="25" cy="25" r="20" fill="none" stroke="#4fa" strokeWidth="4" strokeDasharray="31.4 31.4" strokeLinecap="round">
            <animateTransform attributeName="transform" type="rotate" from="0 25 25" to="360 25 25" dur="0.8s" repeatCount="indefinite" />
          </circle>
        </svg>
      </div>
      <span>{label}…</span>
    </div>
  );
};
