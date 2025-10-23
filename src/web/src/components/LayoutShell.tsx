import React from 'react';
import { Box } from '@mui/material';

interface Props { sidebar: React.ReactNode; rightPanel?: React.ReactNode; children: React.ReactNode; sidebarCollapsed: boolean; }

export const LayoutShell: React.FC<Props> = ({ sidebar, rightPanel, children, sidebarCollapsed }) => {
  return (
    <Box sx={{
      display: 'grid',
      gridTemplateColumns: sidebarCollapsed ? '56px 1fr' : { xs: '1fr', md: '280px 1fr 320px' },
      height: '100%',
      gap: 0,
      position: 'relative'
    }}>
      <Box component="aside" sx={{ display: { xs: 'none', md: 'flex' }, flexDirection: 'column', borderRight: '1px solid', borderColor: 'divider', bgcolor: 'background.paper' }}>{sidebar}</Box>
      <Box component="section" sx={{ display: 'flex', flexDirection: 'column', minHeight: 0 }}>{children}</Box>
      <Box component="aside" sx={{ display: { xs: 'none', md: rightPanel ? 'flex' : 'none' }, flexDirection: 'column', borderLeft: '1px solid', borderColor: 'divider', bgcolor: 'background.paper' }}>{rightPanel}</Box>
    </Box>
  );
};
