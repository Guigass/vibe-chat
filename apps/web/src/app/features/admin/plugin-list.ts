import { InstalledPlugin } from '../../shared/models/chat.models';

export function filterInstalledPlugins(plugins: readonly InstalledPlugin[], query: string): InstalledPlugin[] {
  const needle = query.trim().toLocaleLowerCase();
  if (!needle) {
    return [...plugins];
  }

  return plugins.filter((plugin) => {
    const haystack = [plugin.name, plugin.pluginId, plugin.version, ...plugin.capabilities, ...(plugin.hooks ?? [])]
      .join(' ')
      .toLocaleLowerCase();
    return haystack.includes(needle);
  });
}
