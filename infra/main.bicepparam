using './main.bicep'

param namePrefix = 'reorder'

// Secrets are supplied at deploy time, never committed:
//   Day 1: leave them empty.
//   Day 9: the GitHub Actions pipeline passes them from repository secrets.
param jobKey = readEnvironmentVariable('REORDER_JOB_KEY', '')
param adminKey = readEnvironmentVariable('REORDER_ADMIN_KEY', '')
param webhookUrl = readEnvironmentVariable('REORDER_WEBHOOK_URL', '')
