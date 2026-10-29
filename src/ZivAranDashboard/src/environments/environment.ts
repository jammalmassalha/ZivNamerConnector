export const environment = {
  production: false,
  apiUrl: getBaseUrl() + '/api'
};

function getBaseUrl(): string {
  const base = document.querySelector('base')?.getAttribute('href') || '/';
  return base.replace(/\/$/, ''); // Remove trailing slash
}

