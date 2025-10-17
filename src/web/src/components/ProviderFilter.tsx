import React from 'react';
import { ProviderInfo } from '../types';
import { FormControl, InputLabel, Select, MenuItem, Stack } from '@mui/material';

interface Props {
  providers: ProviderInfo[];
  value: { providerType?: string; providerName?: string };
  onChange: (v: { providerType?: string; providerName?: string }) => void;
}

export const ProviderFilter: React.FC<Props> = ({ providers, value, onChange }) => {
  const types = Array.from(new Set(providers.map(p => p.providerType)));
  const namesForType = providers.filter(p => !value.providerType || p.providerType === value.providerType).map(p => p.providerName);

  return (
    <Stack direction="row" spacing={2} flexWrap="wrap">
      <FormControl size="small" sx={{ minWidth: 160 }}>
        <InputLabel>Provider Type</InputLabel>
        <Select
          label="Provider Type"
          value={value.providerType || ''}
          onChange={e => onChange({ providerType: e.target.value ? String(e.target.value) : undefined, providerName: undefined })}
        >
          <MenuItem value="">All</MenuItem>
          {types.map(t => <MenuItem key={t} value={t}>{t}</MenuItem>)}
        </Select>
      </FormControl>
      <FormControl size="small" sx={{ minWidth: 160 }}>
        <InputLabel>Provider Name</InputLabel>
        <Select
          label="Provider Name"
          value={value.providerName || ''}
          onChange={e => onChange({ providerType: value.providerType, providerName: e.target.value ? String(e.target.value) : undefined })}
        >
          <MenuItem value="">All</MenuItem>
          {namesForType.map(n => <MenuItem key={n} value={n}>{n}</MenuItem>)}
        </Select>
      </FormControl>
    </Stack>
  );
};
