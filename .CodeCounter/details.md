# Details

Date : 2026-06-22 15:54:40

Directory : ./Agent1

Total : 91 files,  13891 codes, 2620 comments, 2471 blanks, all 18982 lines, 15.87% comment rate

[summary](results.md)

## Files
| filename | language | code | comment | blank | total | comment rate |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: |
| [Agent1.csproj](../Agent1\Agent1.csproj) | XML | 37 | 6 | 5 | 48 | 13.95% |
| [Commands\MenuCommands.cs](../Agent1\Commands\MenuCommands.cs) | C# | 340 | 19 | 55 | 414 | 5.29% |
| [Config\AppConfig.cs](../Agent1\Config\AppConfig.cs) | C# | 260 | 86 | 76 | 422 | 24.86% |
| [Config\ModelConfig.cs](../Agent1\Config\ModelConfig.cs) | C# | 23 | 22 | 10 | 55 | 48.89% |
| [Models\ChemicalSubstanceModels.cs](../Agent1\Models\ChemicalSubstanceModels.cs) | C# | 50 | 50 | 35 | 135 | 50.00% |
| [Models\DialogTypes.cs](../Agent1\Models\DialogTypes.cs) | C# | 24 | 0 | 6 | 30 | 0.00% |
| [Models\EvalModels.cs](../Agent1\Models\EvalModels.cs) | C# | 266 | 21 | 104 | 391 | 7.32% |
| [Models\LongTermMemoryModels.cs](../Agent1\Models\LongTermMemoryModels.cs) | C# | 35 | 13 | 13 | 61 | 27.08% |
| [Models\ModuleType.cs](../Agent1\Models\ModuleType.cs) | C# | 16 | 1 | 3 | 20 | 5.88% |
| [Modules\CoTSolidModule.cs](../Agent1\Modules\CoTSolidModule.cs) | C# | 18 | 23 | 4 | 45 | 56.10% |
| [Modules\CoTStreamModule.cs](../Agent1\Modules\CoTStreamModule.cs) | C# | 18 | 24 | 3 | 45 | 57.14% |
| [Modules\ComplianceCheckModule.cs](../Agent1\Modules\ComplianceCheckModule.cs) | C# | 119 | 11 | 18 | 148 | 8.46% |
| [Modules\EmergencyResponseModule.cs](../Agent1\Modules\EmergencyResponseModule.cs) | C# | 104 | 6 | 25 | 135 | 5.45% |
| [Modules\KnowledgeGraphModule.cs](../Agent1\Modules\KnowledgeGraphModule.cs) | C# | 39 | 4 | 10 | 53 | 9.30% |
| [Modules\RAGModule.cs](../Agent1\Modules\RAGModule.cs) | C# | 18 | 21 | 3 | 42 | 53.85% |
| [Modules\ReActSolidModule.cs](../Agent1\Modules\ReActSolidModule.cs) | C# | 18 | 0 | 6 | 24 | 0.00% |
| [Modules\ReActStreamModule.cs](../Agent1\Modules\ReActStreamModule.cs) | C# | 18 | 0 | 6 | 24 | 0.00% |
| [Modules\ReflectionModule.cs](../Agent1\Modules\ReflectionModule.cs) | C# | 18 | 0 | 6 | 24 | 0.00% |
| [Modules\RegulatoryAuditModule.cs](../Agent1\Modules\RegulatoryAuditModule.cs) | C# | 147 | 12 | 28 | 187 | 7.55% |
| [Modules\TicketFollowupModule.cs](../Agent1\Modules\TicketFollowupModule.cs) | C# | 132 | 16 | 25 | 173 | 10.81% |
| [Modules\UnifiedDialogModule.cs](../Agent1\Modules\UnifiedDialogModule.cs) | C# | 84 | 0 | 18 | 102 | 0.00% |
| [Program.cs](../Agent1\Program.cs) | C# | 247 | 350 | 42 | 639 | 58.63% |
| [Services\AI\ILlmService.cs](../Agent1\Services\AI\ILlmService.cs) | C# | 15 | 3 | 4 | 22 | 16.67% |
| [Services\AI\IToolService.cs](../Agent1\Services\AI\IToolService.cs) | C# | 11 | 0 | 1 | 12 | 0.00% |
| [Services\AI\LlmService.cs](../Agent1\Services\AI\LlmService.cs) | C# | 763 | 148 | 145 | 1,056 | 16.25% |
| [Services\AI\MultimodalService.cs](../Agent1\Services\AI\MultimodalService.cs) | C# | 97 | 24 | 18 | 139 | 19.83% |
| [Services\AI\ReflectionVerifier.cs](../Agent1\Services\AI\ReflectionVerifier.cs) | C# | 207 | 37 | 36 | 280 | 15.16% |
| [Services\AI\ToolService.cs](../Agent1\Services\AI\ToolService.cs) | C# | 146 | 15 | 23 | 184 | 9.32% |
| [Services\Compliance\ChemicalComplianceTools.cs](../Agent1\Services\Compliance\ChemicalComplianceTools.cs) | C# | 503 | 57 | 83 | 643 | 10.18% |
| [Services\Compliance\ConclusionVerifier.cs](../Agent1\Services\Compliance\ConclusionVerifier.cs) | C# | 157 | 35 | 16 | 208 | 18.23% |
| [Services\Compliance\EmergencyResponseService.cs](../Agent1\Services\Compliance\EmergencyResponseService.cs) | C# | 199 | 56 | 47 | 302 | 21.96% |
| [Services\Compliance\KnowledgeGraphService.cs](../Agent1\Services\Compliance\KnowledgeGraphService.cs) | C# | 281 | 34 | 58 | 373 | 10.79% |
| [Services\Compliance\RiskAssessmentService.cs](../Agent1\Services\Compliance\RiskAssessmentService.cs) | C# | 109 | 29 | 22 | 160 | 21.01% |
| [Services\Compliance\SafetyGuardService.cs](../Agent1\Services\Compliance\SafetyGuardService.cs) | C# | 98 | 32 | 21 | 151 | 24.62% |
| [Services\Compliance\SensitiveDataMasker.cs](../Agent1\Services\Compliance\SensitiveDataMasker.cs) | C# | 52 | 25 | 18 | 95 | 32.47% |
| [Services\Dialog\AgentDialog.cs](../Agent1\Services\Dialog\AgentDialog.cs) | C# | 253 | 29 | 51 | 333 | 10.28% |
| [Services\Dialog\ChemicalRAG.cs](../Agent1\Services\Dialog\ChemicalRAG.cs) | C# | 472 | 79 | 53 | 604 | 14.34% |
| [Services\Dialog\CoT.cs](../Agent1\Services\Dialog\CoT.cs) | C# | 296 | 50 | 60 | 406 | 14.45% |
| [Services\Dialog\IIntegrationService.cs](../Agent1\Services\Dialog\IIntegrationService.cs) | C# | 28 | 9 | 5 | 42 | 24.32% |
| [Services\Dialog\IntegratedDialogSystem.cs](../Agent1\Services\Dialog\IntegratedDialogSystem.cs) | C# | 333 | 1 | 64 | 398 | 0.30% |
| [Services\Dialog\IntegrationService.cs](../Agent1\Services\Dialog\IntegrationService.cs) | C# | 20 | 2 | 4 | 26 | 9.09% |
| [Services\Dialog\IntentRouter.cs](../Agent1\Services\Dialog\IntentRouter.cs) | C# | 38 | 8 | 10 | 56 | 17.39% |
| [Services\Dialog\RAG.cs](../Agent1\Services\Dialog\RAG.cs) | C# | 330 | 91 | 66 | 487 | 21.62% |
| [Services\Dialog\RunReflectionStreamTools.cs](../Agent1\Services\Dialog\RunReflectionStreamTools.cs) | C# | 279 | 21 | 48 | 348 | 7.00% |
| [Services\Dialog\SessionManager.cs](../Agent1\Services\Dialog\SessionManager.cs) | C# | 192 | 12 | 30 | 234 | 5.88% |
| [Services\Eval\EvalEngine.cs](../Agent1\Services\Eval\EvalEngine.cs) | C# | 778 | 104 | 117 | 999 | 11.79% |
| [Services\Infrastructure\AuditService.cs](../Agent1\Services\Infrastructure\AuditService.cs) | C# | 129 | 11 | 24 | 164 | 7.86% |
| [Services\Infrastructure\DatabaseService.cs](../Agent1\Services\Infrastructure\DatabaseService.cs) | C# | 1,092 | 34 | 128 | 1,254 | 3.02% |
| [Services\Infrastructure\IAuditService.cs](../Agent1\Services\Infrastructure\IAuditService.cs) | C# | 20 | 6 | 2 | 28 | 23.08% |
| [Services\Infrastructure\IDatabaseService.cs](../Agent1\Services\Infrastructure\IDatabaseService.cs) | C# | 36 | 16 | 12 | 64 | 30.77% |
| [Services\Infrastructure\IInferenceModule.cs](../Agent1\Services\Infrastructure\IInferenceModule.cs) | C# | 9 | 17 | 1 | 27 | 65.38% |
| [Services\Infrastructure\IModuleFactory.cs](../Agent1\Services\Infrastructure\IModuleFactory.cs) | C# | 9 | 0 | 2 | 11 | 0.00% |
| [Services\Infrastructure\ISessionService.cs](../Agent1\Services\Infrastructure\ISessionService.cs) | C# | 13 | 0 | 0 | 13 | 0.00% |
| [Services\Infrastructure\MetricsCollector.cs](../Agent1\Services\Infrastructure\MetricsCollector.cs) | C# | 113 | 21 | 18 | 152 | 15.67% |
| [Services\Infrastructure\ModuleDispatcher.cs](../Agent1\Services\Infrastructure\ModuleDispatcher.cs) | C# | 34 | 15 | 5 | 54 | 30.61% |
| [Services\Infrastructure\ModuleFactory.cs](../Agent1\Services\Infrastructure\ModuleFactory.cs) | C# | 66 | 2 | 8 | 76 | 2.94% |
| [Services\Infrastructure\SessionService.cs](../Agent1\Services\Infrastructure\SessionService.cs) | C# | 34 | 0 | 6 | 40 | 0.00% |
| [Services\Knowledge\ChemicalDocumentRecord.cs](../Agent1\Services\Knowledge\ChemicalDocumentRecord.cs) | C# | 23 | 28 | 19 | 70 | 54.90% |
| [Services\Knowledge\ChemicalSubstanceDatabase.cs](../Agent1\Services\Knowledge\ChemicalSubstanceDatabase.cs) | C# | 847 | 70 | 66 | 983 | 7.63% |
| [Services\Knowledge\DocExtractor.cs](../Agent1\Services\Knowledge\DocExtractor.cs) | C# | 146 | 45 | 38 | 229 | 23.56% |
| [Services\Knowledge\GpuVectorIndexService.cs](../Agent1\Services\Knowledge\GpuVectorIndexService.cs) | C# | 281 | 39 | 42 | 362 | 12.19% |
| [Services\Knowledge\HybridKnowledgeBaseService.cs](../Agent1\Services\Knowledge\HybridKnowledgeBaseService.cs) | C# | 512 | 46 | 84 | 642 | 8.24% |
| [Services\Knowledge\IKnowledgeBaseService.cs](../Agent1\Services\Knowledge\IKnowledgeBaseService.cs) | C# | 19 | 3 | 5 | 27 | 13.64% |
| [Services\Knowledge\KnowledgeBaseService.cs](../Agent1\Services\Knowledge\KnowledgeBaseService.cs) | C# | 459 | 161 | 81 | 701 | 25.97% |
| [Services\Knowledge\PdfExtractor.cs](../Agent1\Services\Knowledge\PdfExtractor.cs) | C# | 136 | 46 | 38 | 220 | 25.27% |
| [Services\Knowledge\QueryCacheService.cs](../Agent1\Services\Knowledge\QueryCacheService.cs) | C# | 138 | 32 | 23 | 193 | 18.82% |
| [Services\Knowledge\RerankerService.cs](../Agent1\Services\Knowledge\RerankerService.cs) | C# | 151 | 38 | 25 | 214 | 20.11% |
| [Services\Knowledge\RetrievedChunk.cs](../Agent1\Services\Knowledge\RetrievedChunk.cs) | C# | 37 | 0 | 7 | 44 | 0.00% |
| [Services\Knowledge\SemanticChunker.cs](../Agent1\Services\Knowledge\SemanticChunker.cs) | C# | 227 | 54 | 46 | 327 | 19.22% |
| [Services\Knowledge\TextCleaner.cs](../Agent1\Services\Knowledge\TextCleaner.cs) | C# | 175 | 63 | 39 | 277 | 26.47% |
| [Services\Logging\Enrichers\EnvironmentEnricher.cs](../Agent1\Services\Logging\Enrichers\EnvironmentEnricher.cs) | C# | 18 | 5 | 5 | 28 | 21.74% |
| [Services\Logging\Enrichers\RunIdEnricher.cs](../Agent1\Services\Logging\Enrichers\RunIdEnricher.cs) | C# | 15 | 4 | 5 | 24 | 21.05% |
| [Services\Logging\Enrichers\SessionEnricher.cs](../Agent1\Services\Logging\Enrichers\SessionEnricher.cs) | C# | 12 | 12 | 4 | 28 | 50.00% |
| [Services\Logging\Enrichers\ThreadEnricher.cs](../Agent1\Services\Logging\Enrichers\ThreadEnricher.cs) | C# | 12 | 4 | 4 | 20 | 25.00% |
| [Services\Logging\Filters\KeywordLogFilter.cs](../Agent1\Services\Logging\Filters\KeywordLogFilter.cs) | C# | 40 | 14 | 7 | 61 | 25.93% |
| [Services\Logging\RunIdGenerator.cs](../Agent1\Services\Logging\RunIdGenerator.cs) | C# | 13 | 10 | 6 | 29 | 43.48% |
| [Services\Logging\Sinks\AlertSink.cs](../Agent1\Services\Logging\Sinks\AlertSink.cs) | C# | 22 | 7 | 8 | 37 | 24.14% |
| [Services\Memory\FactExtractor.cs](../Agent1\Services\Memory\FactExtractor.cs) | C# | 90 | 9 | 11 | 110 | 9.09% |
| [Services\Memory\ILongTermMemoryService.cs](../Agent1\Services\Memory\ILongTermMemoryService.cs) | C# | 22 | 18 | 14 | 54 | 45.00% |
| [Services\Memory\IMemoryService.cs](../Agent1\Services\Memory\IMemoryService.cs) | C# | 22 | 14 | 6 | 42 | 38.89% |
| [Services\Memory\LongTermMemoryService.cs](../Agent1\Services\Memory\LongTermMemoryService.cs) | C# | 144 | 16 | 29 | 189 | 10.00% |
| [Services\Memory\MemoryCoordinator.cs](../Agent1\Services\Memory\MemoryCoordinator.cs) | C# | 170 | 36 | 22 | 228 | 17.48% |
| [Services\Memory\MemoryService.cs](../Agent1\Services\Memory\MemoryService.cs) | C# | 400 | 45 | 65 | 510 | 10.11% |
| [Services\Memory\ResponseCacheService.cs](../Agent1\Services\Memory\ResponseCacheService.cs) | C# | 158 | 4 | 23 | 185 | 2.47% |
| [Services\Monitoring\AlertDispatcher.cs](../Agent1\Services\Monitoring\AlertDispatcher.cs) | C# | 39 | 18 | 8 | 65 | 31.58% |
| [Services\Monitoring\ConsoleAlertService.cs](../Agent1\Services\Monitoring\ConsoleAlertService.cs) | C# | 31 | 4 | 6 | 41 | 11.43% |
| [Services\Monitoring\EmailAlertService.cs](../Agent1\Services\Monitoring\EmailAlertService.cs) | C# | 82 | 16 | 11 | 109 | 16.33% |
| [Services\Monitoring\IAlertService.cs](../Agent1\Services\Monitoring\IAlertService.cs) | C# | 12 | 17 | 4 | 33 | 58.62% |
| [Services\Security\DeviceFingerprintService.cs](../Agent1\Services\Security\DeviceFingerprintService.cs) | C# | 12 | 13 | 3 | 28 | 52.00% |
| [Services\Security\TokenBlacklistService.cs](../Agent1\Services\Security\TokenBlacklistService.cs) | C# | 38 | 21 | 9 | 68 | 35.59% |
| [appsettings.json](../Agent1\appsettings.json) | JSON | 145 | 0 | 1 | 146 | 0.00% |

[summary](results.md)