import 'reflect-metadata';
import { Controller, Get, Module, ServiceUnavailableException } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { FastifyAdapter, NestFastifyApplication } from '@nestjs/platform-fastify';
import { ApiOkResponse, ApiProperty, ApiResponse, DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { Runner } from './runner';

class HealthEnvelope {
  @ApiProperty() success!: boolean;
  @ApiProperty({ nullable: true, type: String }) data!: string | null;
  @ApiProperty({ nullable: true, type: String }) error!: string | null;
  @ApiProperty() code!: number;
}
class WorkerMetrics {
  @ApiProperty() activeRequests!: number;
  @ApiProperty() providerCalls!: number;
  @ApiProperty() cancellations!: number;
  @ApiProperty() deliveryRetries!: number;
}
class MetricsEnvelope {
  @ApiProperty() success!: boolean;
  @ApiProperty({ type: WorkerMetrics }) data!: WorkerMetrics;
  @ApiProperty({ nullable: true, type: String }) error!: string | null;
  @ApiProperty() code!: number;
}
@Controller()
class HealthController {
  constructor(private readonly runner: Runner) {}
  @Get('health') @ApiOkResponse({ type: HealthEnvelope })
  health(): HealthEnvelope { return { success: true, data: 'healthy', error: null, code: 200 }; }
  @Get('ready') @ApiOkResponse({ type: HealthEnvelope }) @ApiResponse({ status: 503, type: HealthEnvelope })
  ready(): HealthEnvelope {
    if (!this.runner.ready) throw new ServiceUnavailableException({ success: false, data: null, error: 'NOT_READY', code: 503 });
    return { success: true, data: 'ready', error: null, code: 200 };
  }
  @Get('metrics') @ApiOkResponse({ type: MetricsEnvelope })
  metrics(): MetricsEnvelope {
    return { success: true, error: null, code: 200, data: {
      activeRequests: this.runner.activeRequests, providerCalls: this.runner.calls,
      cancellations: this.runner.cancellations, deliveryRetries: this.runner.deliveryRetries,
    }};
  }
}
@Module({ controllers: [HealthController], providers: [Runner] })
class WorkerModule {}
async function main() {
  const app = await NestFactory.create<NestFastifyApplication>(WorkerModule, new FastifyAdapter({ logger: false }));
  const runner = app.get(Runner);
  const document = SwaggerModule.createDocument(app, new DocumentBuilder().setTitle('Private Tarot Worker').setVersion('1').build());
  if (runner.config.NODE_ENV === 'development') SwaggerModule.setup('swagger', app, document, { jsonDocumentUrl: 'swagger/v1/swagger.json' });
  app.enableShutdownHooks();
  await app.listen(runner.config.PORT, '0.0.0.0');
  void runner.start();
}
void main();
