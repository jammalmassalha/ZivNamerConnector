export const environment = {
  production: true,
  apiUrl: getBaseUrl() + '/api'
};

function getBaseUrl(): string {
  // When deployed at /pbi_srv/zivarantest/, this reads the <base href> tag
  // and returns /pbi_srv/zivarantest (without trailing slash)
  const base = document.querySelector('base')?.getAttribute('href') || '/';
  return base.replace(/\/$/, ''); // Remove trailing slash
}

