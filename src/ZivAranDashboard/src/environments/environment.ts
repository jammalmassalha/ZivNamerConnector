export const environment = {
  production: false,
  apiUrl: getBaseUrl() + '/api',
  hubUrl: getBaseUrl() + '/hubs/aran'
};

function getBaseUrl(): string {
  const base = document.querySelector('base')?.getAttribute('href') || '/';
  return base.replace(/\/$/, ''); // Remove trailing slash
}

