import { E2eeApiService } from '../api/e2ee-api.service';
import { createDevice, E2eePublicDevice, loadDevice, sealFile, sealText } from './e2ee-crypto';

type E2eeChannelClient = Pick<E2eeApiService, 'registerE2eeDevice' | 'e2eeChannel'>;

async function recipients(api: E2eeChannelClient, channelId: string): Promise<{ epoch: number; keys: E2eePublicDevice[] }> {
  const record = loadDevice() ?? (await createDevice());
  await api.registerE2eeDevice(record.deviceId, record.publicJwk);
  const state = await api.e2eeChannel(channelId);
  const keys = state.devices.map((device) => ({
    deviceId: device.deviceId,
    publicJwk: typeof device.publicJwk === 'string'
      ? (JSON.parse(device.publicJwk) as JsonWebKey)
      : device.publicJwk,
  }));
  if (!keys.some((key) => key.deviceId === record.deviceId)) {
    throw new Error('E2eeDeviceRequired');
  }
  return { epoch: state.generation, keys };
}

export async function sealChannelText(api: E2eeChannelClient, channelId: string, text: string): Promise<string> {
  const ready = await recipients(api, channelId);
  return sealText(text, ready.epoch, ready.keys);
}

export async function sealChannelFile(api: E2eeChannelClient, channelId: string, file: File): Promise<File> {
  const ready = await recipients(api, channelId);
  return sealFile(file, ready.epoch, ready.keys);
}
