import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

jest.mock('../../api', () => ({
  __esModule: true,
  listProviders: jest.fn(),
  updateProvider: jest.fn(),
  deleteProvider: jest.fn(),
  probeProvider: jest.fn(),
  createProvider: jest.fn(),
}));

import { listProviders, createProvider } from '../../api';
import { ProvidersPage } from '../ProvidersPage';

const mockedListProviders = listProviders as jest.MockedFunction<typeof listProviders>;
const mockedCreateProvider = createProvider as jest.MockedFunction<typeof createProvider>;

describe('ProvidersPage', () => {
  beforeEach(() => {
    mockedListProviders.mockResolvedValue({ providers: [], count: 0 });
    mockedCreateProvider.mockResolvedValue(undefined);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  test('switching provider template updates defaults', async () => {
    const user = userEvent.setup();

    render(<ProvidersPage />);

    await waitFor(() => expect(mockedListProviders).toHaveBeenCalledTimes(1));

    const addButton = await screen.findByRole('button', { name: /add provider/i });
    await user.click(addButton);

    const typeSelect = screen.getByLabelText('Provider Type') as HTMLSelectElement;
    const nameField = screen.getByLabelText('Provider Name') as HTMLInputElement;
    const settingsField = screen.getByRole('textbox', { name: 'Settings JSON' }) as HTMLTextAreaElement;

    expect(settingsField.value).toContain('"name": "LocalFiles"');
    expect(settingsField.value).toContain('"enabled": true');

    await user.selectOptions(typeSelect, 's3');

    await waitFor(() => expect(nameField).toHaveValue('S3'));
    await waitFor(() => {
      expect(JSON.parse(settingsField.value).bucketName).toBe('your-bucket-name');
    });

    await user.clear(nameField);
    await user.type(nameField, 'CustomS3');

    await waitFor(() => {
      expect(JSON.parse(settingsField.value).name).toBe('CustomS3');
    });
  });

  test('creates provider with hydrated payload', async () => {
    const user = userEvent.setup();

    render(<ProvidersPage />);

    await waitFor(() => expect(mockedListProviders).toHaveBeenCalledTimes(1));

    const addButton = await screen.findByRole('button', { name: /add provider/i });
    await user.click(addButton);

    const typeSelect = screen.getByLabelText('Provider Type') as HTMLSelectElement;
    const nameField = screen.getByLabelText('Provider Name') as HTMLInputElement;
    const enabledSwitch = screen.getByRole('checkbox', { name: /enabled/i });
    const settingsField = screen.getByRole('textbox', { name: 'Settings JSON' }) as HTMLTextAreaElement;

    await user.selectOptions(typeSelect, 's3');
    await waitFor(() => expect(nameField).toHaveValue('S3'));

    await user.clear(nameField);
    await user.type(nameField, 'MyS3');

    await waitFor(() => {
      expect(JSON.parse(settingsField.value).name).toBe('MyS3');
    });

    await user.click(enabledSwitch);

    const createButton = screen.getByRole('button', { name: /create/i });
    await user.click(createButton);

    await waitFor(() => expect(mockedCreateProvider).toHaveBeenCalledTimes(1));

    const [providerType, providerName, payload] = mockedCreateProvider.mock.calls[0];

    expect(providerType).toBe('s3');
    expect(providerName).toBe('MyS3');
    expect(payload).toMatchObject({
      name: 'MyS3',
      enabled: false,
      bucketName: 'your-bucket-name',
    });

    await waitFor(() => expect(mockedListProviders).toHaveBeenCalledTimes(2));
  });
});
