import React from 'react';
// DEPRECATED: This component has been replaced by the AppBar Autocomplete multi-select.
// Retained temporarily for reference; remove once no longer needed.
import { ProviderInfo } from '../types';
import { FormControl, FormLabel, FormGroup, FormControlLabel, Checkbox, Stack, Box } from '@mui/material';

interface Props {
  providers: ProviderInfo[];
  value: string[];
  onChange: (v: string[]) => void;
}

export const ProviderFilter: React.FC<Props> = ({ providers, value, onChange }) => {
  const handleToggle = (providerName: string) => {
    const newValue = value.includes(providerName)
      ? value.filter(n => n !== providerName)
      : [...value, providerName];
    onChange(newValue);
  };

  return (
    <FormControl component="fieldset" variant="standard">
      <FormLabel component="legend" sx={{ mb: 1, fontSize: '0.875rem', fontWeight: 500 }}>
        Select Providers
      </FormLabel>
      <FormGroup>
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
          {providers.map(p => (
            <FormControlLabel
              key={`${p.providerType}-${p.providerName}`}
              control={
                <Checkbox
                  size="small"
                  checked={value.includes(p.providerName)}
                  onChange={() => handleToggle(p.providerName)}
                />
              }
              label={`${p.providerName} (${p.providerType})`}
              sx={{ mr: 2 }}
            />
          ))}
        </Box>
      </FormGroup>
    </FormControl>
  );
};
