import { z } from 'zod';

export const workSchema = z.object({
  version: z.literal(1), kind: z.enum(['REQUEST', 'CANCEL']), jobId: z.uuid(), attemptId: z.uuid(),
  correlationId: z.uuid(), deadline: z.iso.datetime({ offset: true }), providerRef: z.string().min(1).max(100),
}).strict().refine(x => x.jobId === x.correlationId);
export type Work = z.infer<typeof workSchema>;
export const resultSchema = z.object({
  version: z.literal(1), kind: z.enum(['RESULT', 'FAILURE']), jobId: z.uuid(), attemptId: z.uuid(),
  correlationId: z.uuid(), deadline: z.iso.datetime({ offset: true }), providerRef: z.string().min(1).max(100),
  body: z.string().max(262144).nullable(), errorCode: z.string().max(100).nullable(),
}).strict().refine(x => x.jobId === x.correlationId && (x.kind === 'RESULT' ? x.body !== null && x.errorCode === null : x.body === null && x.errorCode !== null));
export const configSchema = z.object({
  BROKER_URI: z.url().refine(x => /^amqps?:/.test(x)), API_URL: z.url(),
  WORKER_KEY: z.string().min(32), PROVIDER_REF: z.string().default('cloud-default'),
  PROVIDER_URL: z.url().refine(x => x.startsWith('https:') || /^http:\/\/(localhost|127\.0\.0\.1)(:|\/)/.test(x)),
  PROVIDER_KEY: z.string().min(1), PROVIDER_MODEL: z.string().min(1),
  PROVIDER_IDEMPOTENCY_HEADER: z.enum(['', 'Idempotency-Key', 'X-Idempotency-Key']).default(''),
  PREFETCH: z.coerce.number().int().min(1).max(16).default(4),
  PORT: z.coerce.number().int().min(1).max(65535).default(3100),
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
});
export type Config = z.infer<typeof configSchema>;
