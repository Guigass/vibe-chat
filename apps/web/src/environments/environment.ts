import { publicConfig } from './public-config.generated';
import { webVersion } from './version.generated';

export const environment = {
  production: true,
  apiUrl: publicConfig.apiUrl,
  hubUrl: publicConfig.hubUrl,
  keycloak: {
    authority: publicConfig.keycloakAuthority,
    clientId: publicConfig.keycloakClientId,
    redirectUri: typeof window !== 'undefined' ? `${window.location.origin}/auth/callback` : 'http://localhost:4200/auth/callback',
    postLogoutRedirectUri: typeof window !== 'undefined' ? window.location.origin : 'http://localhost:4200',
    silentRedirectUri: typeof window !== 'undefined' ? `${window.location.origin}/auth/silent-renew` : 'http://localhost:4200/auth/silent-renew',
    scope: 'openid profile email offline_access',
  },
  grafanaUrl: publicConfig.grafanaUrl,
  // D-06: UI opt-in; production builds keep summarize hidden unless explicitly enabled.
  aiSummarizeEnabled: false,
  aiTranscribeEnabled: false,
  // DevAuth / offline demo — only for local Development builds.
  enableDevAuth: false,
  appVersion: webVersion.version,
  buildId: webVersion.buildId,
};
