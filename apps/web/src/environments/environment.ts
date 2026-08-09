export const environment = {
  production: true,
  apiUrl: 'http://localhost:5080',
  hubUrl: 'http://localhost:5080/hubs/chat',
  keycloak: {
    authority: 'http://localhost:8080/realms/vibechat',
    clientId: 'vibechat-web',
    redirectUri: typeof window !== 'undefined' ? `${window.location.origin}/auth/callback` : 'http://localhost:4200/auth/callback',
    postLogoutRedirectUri: typeof window !== 'undefined' ? window.location.origin : 'http://localhost:4200',
    silentRedirectUri: typeof window !== 'undefined' ? `${window.location.origin}/auth/silent-renew` : 'http://localhost:4200/auth/silent-renew',
    scope: 'openid profile email offline_access',
  },
  grafanaUrl: 'http://localhost:3000',
  // D-06: UI opt-in; production builds keep summarize hidden unless explicitly enabled.
  aiSummarizeEnabled: false,
  aiTranscribeEnabled: false,
  appVersion: '0.1.0',
};
