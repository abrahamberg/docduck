import { useState, useEffect, useCallback } from 'react';
import { QueryResponse } from '../types';
import { HistoryItem } from '../components/HistoryList';

const KEY = 'dd:history';
const MAX = 20;

export function useHistory() {
  const [items, setItems] = useState<HistoryItem[]>(() => {
    try {
      const raw = localStorage.getItem(KEY);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) return parsed as HistoryItem[];
      return [];
    } catch {
      return [];
    }
  });

  useEffect(() => {
    localStorage.setItem(KEY, JSON.stringify(items));
  }, [items]);

  const add = useCallback(
    (
      question: string,
      resp: QueryResponse | null,
      providers: string[],
      meta?: { latencyMs?: number; tokensUsed?: number }
    ) => {
      const answerSnippet = resp?.answer ? resp.answer.slice(0, 120) : undefined;
      setItems((prev) => [
        {
          id: crypto.randomUUID(),
          question,
          answerSnippet,
          timestamp: Date.now(),
          providers,
          latencyMs: meta?.latencyMs,
          tokensUsed: meta?.tokensUsed ?? resp?.tokensUsed,
        },
        ...prev.slice(0, MAX - 1),
      ]);
    },
    []
  );

  return { items, add };
}
