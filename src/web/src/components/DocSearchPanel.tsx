import React, { useState } from 'react';
import { QueryRequest, DocumentResult } from '../types';
import { postDocSearch } from '../api';
import { Box, Stack, TextField, Button, Card, CardContent, Typography, CircularProgress, Tooltip } from '@mui/material';
import { DocSearchResults } from './DocSearchResults';

interface Props {
  providerType?: string;
  providerName?: string;
}

export const DocSearchPanel: React.FC<Props> = ({ providerType, providerName }) => {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<DocumentResult[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function doSearch() {
    if (!query.trim()) return;
    setLoading(true);
    setError(null);
    setResults(null);
    try {
      const data = await postDocSearch({ question: query, providerType, providerName });
      setResults(data.results);
    } catch (e: any) {
      setError(e.message || 'Error');
    } finally {
      setLoading(false);
    }
  }

  return (
    <Stack sx={{ height: '100%' }}>
      <Box sx={{ p: 2, flex: 1, overflowY: 'auto' }}>
        <Stack spacing={2}>
          <TextField
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder="Search for documents…"
            multiline
            minRows={1}
            maxRows={4}
            fullWidth
          />
          <Stack direction="row" spacing={1}>
            <Tooltip title="Search documents">
              <span>
                <Button variant="contained" disabled={loading || !query.trim()} onClick={doSearch}>Search</Button>
              </span>
            </Tooltip>
            <Tooltip title="Clear">
              <span>
                <Button variant="outlined" color="inherit" disabled={loading && !results} onClick={() => { setQuery(''); setResults(null); }}>Clear</Button>
              </span>
            </Tooltip>
            {loading && <CircularProgress size={24} />}
          </Stack>
          {error && <Typography color="error" variant="body2">{error}</Typography>}
          {results && (
            <Card variant="outlined">
              <CardContent>
                <Typography variant="h6" gutterBottom>Document Search Results</Typography>
                <DocSearchResults results={results} />
              </CardContent>
            </Card>
          )}
        </Stack>
      </Box>
    </Stack>
  );
};
