import 'dotenv/config';

function required(key: string): string {
  const val = process.env[key];
  if (!val) throw new Error(`Missing required environment variable: ${key}`);
  return val;
}

function optional(key: string, fallback: string): string {
  return process.env[key] ?? fallback;
}

export const env = {
  PORT: parseInt(optional('PORT', '3001'), 10),
  NODE_ENV: optional('NODE_ENV', 'development'),
  DATABASE_URL: optional('DATABASE_URL', 'file:./dev.db'),
  REDIS_URL: optional('REDIS_URL', ''),
  JWT_SECRET: optional('JWT_SECRET', 'dev_secret_change_in_production_please'),
  JWT_REFRESH_SECRET: optional('JWT_REFRESH_SECRET', 'dev_refresh_secret_change_in_production'),
  JWT_EXPIRES_IN: optional('JWT_EXPIRES_IN', '15m'),
  JWT_REFRESH_EXPIRES_IN: optional('JWT_REFRESH_EXPIRES_IN', '30d'),
  ALLOWED_ORIGINS: optional('ALLOWED_ORIGINS', 'http://localhost:3000,http://localhost:5173').split(','),
  DISCONNECT_GRACE_PERIOD_MS: parseInt(optional('DISCONNECT_GRACE_PERIOD_MS', '120000'), 10),
  AI_AUTOFILL_DELAY_MS: parseInt(optional('AI_AUTOFILL_DELAY_MS', '30000'), 10),
  isProduction: optional('NODE_ENV', 'development') === 'production',
  isDevelopment: optional('NODE_ENV', 'development') === 'development',
};
