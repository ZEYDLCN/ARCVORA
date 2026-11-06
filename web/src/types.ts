export type Origin = 'user_statement' | 'system_proposal' | 'assumption';
export type ReviewStatus = 'proposed' | 'confirmed' | 'rejected' | 'deferred';
export type Priority = 'critical' | 'high' | 'medium' | 'low';
export type RequirementKind = 'functional' | 'quality' | 'constraint' | 'existing_decision';

export interface Reviewable { id: string; origin: Origin; reviewStatus: ReviewStatus }
export interface SourceRef { sourceId: string; excerpt?: string }
export interface Criterion { id: string; text: string }
export interface Source { id: string; title: string; text: string; format: string }
export interface Requirement extends Reviewable {
  text: string; kind: RequirementKind; priority: Priority; sourceRefs: SourceRef[]; acceptanceCriteria: Criterion[];
}
export interface Assumption extends Reviewable { text: string; rationale: string; status: 'unverified' | 'verified' | 'invalidated' }
export interface Decision extends Reviewable {
  title: string; choice: string; alternatives: { name: string; tradeoff: string }[]; rationale: string; consequences: string;
}
export interface Component extends Reviewable {
  name: string; kind: string; responsibility: string; technology?: string;
}
export interface Relationship extends Reviewable {
  sourceId: string; targetId: string; communication: string; description: string; failureBehavior?: string;
}
export interface Entity extends Reviewable {
  name: string; ownerComponentId?: string;
  fields: { name: string; type: string; isKey: boolean; isNullable: boolean; notes?: string }[];
  relations: { targetEntityId: string; cardinality: string; label: string }[]; constraints: string[];
}
export interface Actor extends Reviewable { name: string; description: string }
export interface FlowStep { from: string; to: string; message: string; kind: string; altId?: string; condition?: string }
export interface Flow extends Reviewable { name: string; description: string; participants: string[]; steps: FlowStep[] }
export interface Endpoint extends Reviewable { method: string; path: string; summary: string; componentId?: string; authorization?: string }
export interface TraceLink extends Reviewable { fromId: string; toId: string; kind: 'addresses' | 'implements' | 'depends_on' | 'assumes' | 'blocked_by' }
export interface OpenQuestion extends Reviewable {
  text: string; impact: Priority; status: 'open' | 'answered' | 'deferred'; answer?: string;
}
export interface Model {
  schemaVersion: string;
  project: { name: string; description: string; techProfileId: string };
  sources: Source[]; requirements: Requirement[]; assumptions: Assumption[]; decisions: Decision[];
  components: Component[]; relationships: Relationship[]; entities: Entity[]; actors: Actor[];
  flows: Flow[]; endpoints: Endpoint[]; traceLinks: TraceLink[]; openQuestions: OpenQuestion[];
}

export type FindingKind = 'schema' | 'reference' | 'consistency' | 'traceability' | 'profile' | 'review_suggestion';
export interface Finding {
  ruleId: string; kind: FindingKind; severity: 'error' | 'warning' | 'info'; objectId?: string; message: string; basis?: string;
}

export interface VersionInfo {
  id: string; number: number; summary: string; kind: string; contentHash: string; createdBy: string; createdAt: string;
}
export interface ProjectSummary {
  id: string; workspaceId: string; name: string; description: string; techProfileId: string; headNumber: number;
  updatedAt: string; pendingProposals: number; role: string;
}
export interface ProjectDetail {
  summary: ProjectSummary; head: VersionInfo; model: Model; findings: Finding[];
  canEdit: boolean; canAccept: boolean; canAdmin: boolean;
}
export interface View { kind: 'component' | 'sequence' | 'er'; flowId?: string; title: string; mermaid: string }

export interface ProposalSummary {
  id: string; kind: string; title: string; requestText?: string; status: 'pending' | 'accepted' | 'rejected' | 'stale';
  baseNumber: number; provider: string; isAi: boolean; note?: string; tokensUsed: number; repairsUsed: number;
  createdAt: string; operationCount: number; isStale: boolean; acceptedVersionId?: string;
}
export type ImpactLevel = 'direct' | 'dependent' | 'possible' | 'uncertain';
export interface ImpactItem {
  type: string; id: string; title: string; level: ImpactLevel; change: 'added' | 'changed' | 'removed' | 'none';
  reason: string; via?: string; groupId?: string; blockers: string[];
}
export interface ProposalDetail {
  summary: ProposalSummary;
  patch: { summary: string; operations: { op: string; type: string; id: string; groupId?: string }[] };
  impact: {
    items: ImpactItem[]; areas: { area: string; direct: number; dependent: number; possible: number; uncertain: number }[];
    openQuestionIds: string[];
  };
  newFindings: Finding[];
  groups: { id: string; title: string; description: string; operationCount: number; dependsOn: string[] }[];
}
export interface Job {
  id: string; projectId: string; kind: string; status: 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled';
  stage?: string; attempts: number; maxAttempts: number; errorCode?: string; error?: string; problems: string[];
  proposalId?: string; tokensUsed: number; tokenBudget: number; cancelRequested: boolean;
}
export interface ProfileInfo {
  id: string; name: string; status: string; selectable: boolean; supportedVersionNote: string; verification: string;
}
export interface WorkspaceInfo { id: string; name: string; role: string; canAccept: boolean; isAccounting?: boolean }
export interface AuthResult {
  token: string; user: { id: string; email: string; displayName: string }; workspaces: WorkspaceInfo[]; mustChangePassword?: boolean;
}
export interface Artifact { id: string; versionNumber: number; format: string; fileName: string; contentHash: string; modelHash: string; size: number }

// ------------------------------------------------------------------ Akış uygulamaları (Apps)
import type { FlowDefinition } from './lib/flow';
export type { FlowDefinition } from './lib/flow';

export interface FlowIssue { code: string; path: string; message: string }

export interface AppSummary {
  id: string; workspaceId: string; name: string; description: string; icon: string; status: 'draft' | 'published' | 'archived';
  versionNumber: number; hasDraftChanges: boolean; revision: number; updatedAt: string;
}
export interface AppDetail { summary: AppSummary; draft: FlowDefinition; published?: FlowDefinition; issues: FlowIssue[] }
export interface MyApp { id: string; workspaceId: string; name: string; description: string; icon: string; versionNumber: number }
export interface AppForm { app: MyApp; definition: FlowDefinition }
export interface AppGrants { userIds: string[]; groupIds: string[] }

export type RequestStatus = 'in_review' | 'approved' | 'rejected' | 'cancelled';
export type TaskStatus = 'pending' | 'approved' | 'rejected' | 'skipped';
export interface RequestSummary {
  id: string; workspaceId: string; number: string; appId: string; appName: string; icon: string; status: RequestStatus; summary: string;
  requesterId: string; requesterName: string; createdAt: string; completedAt?: string; currentStepName?: string;
}
export interface TaskInfo {
  id: string; assigneeId: string; stepIndex: number; stepId: string; stepName: string; status: TaskStatus; comment?: string;
  fallback?: string; createdAt: string; decidedAt?: string; decidedBy?: string;
}
export interface RequestEventInfo { id: number; at: string; actorId?: string; kind: string; detail?: Record<string, unknown> }
export interface AttachmentInfo {
  id: string; fieldKey?: string; fileName: string; contentType: string; size: number; sha256: string; uploadedBy: string; createdAt: string;
}
export interface RequestDetail {
  summary: RequestSummary; definition: FlowDefinition; versionNumber: number; data: Record<string, unknown>;
  tasks: TaskInfo[]; events: RequestEventInfo[]; people: Record<string, string>; canCancel: boolean; myPendingTaskId?: string;
  attachments: AttachmentInfo[];
}
export interface MyTask {
  id: string; requestId: string; requestNumber: string; appName: string; icon: string; summary: string; requesterName: string;
  stepName: string; status: TaskStatus; fallback?: string; comment?: string; createdAt: string; decidedAt?: string;
  /** Görev başkasına atanmış ve ben vekil olarak görüyorsam: asıl onaylayıcı. */
  onBehalfOfId?: string; onBehalfOfName?: string;
}
export interface Member { userId: string; email: string; displayName: string; role: string; canAccept: boolean; managerId?: string; mustChangePassword: boolean; passwordResettable: boolean }
export interface GroupInfo { id: string; name: string; memberIds: string[] }
export interface DirectoryEntry { id: string; displayName: string }
export interface AccountingEntry {
  id: string; requestId: string; number: string; appId: string; appName: string; category: string; status: 'pending' | 'sent' | 'processed' | 'failed';
  attempts: number; lastError?: string; createdAt: string; sentAt?: string; processedBy?: string; processedByName?: string; processedAt?: string;
  note?: string; payload: Record<string, unknown>;
}
export interface AccountingSettings { webhookUrl?: string; hasSecret: boolean; accountingGroupId?: string }

export interface VerifyResult { valid: boolean; number?: string; status?: RequestStatus; statusLabel?: string; completedAt?: string; appName?: string }
export interface DemoStatus { present: boolean; users: number; groups: number; apps: number; requests: number; attachments: number }
export interface DemoUser { id: string; email: string; displayName: string; title: string; temporaryPassword: string }
export interface DemoSeed { users: DemoUser[]; groups: number; apps: number; requests: number; attachments: number }
export type DelegationStatus = 'upcoming' | 'active' | 'expired' | 'revoked';
export interface Delegation {
  id: string; fromUserId: string; fromName: string; toUserId: string; toName: string; startsOn: string; endsOn: string;
  reason?: string; status: DelegationStatus; createdAt: string; revokedAt?: string;
}
export interface EmailStatus { configured: boolean; emailEnabled: boolean; remindersEnabled: boolean; pending: number; failed: number; from?: string }
export interface EmailOutboxRow { id: string; to: string; kind: string; subject: string; status: 'pending' | 'sent' | 'failed' | 'skipped'; attempts: number; lastError?: string; createdAt: string; sentAt?: string }
export interface SearchPage { items: RequestSummary[]; total: number; page: number; pageSize: number }
export interface AppReportRow { appId: string; name: string; total: number; inReview: number; approved: number; rejected: number; cancelled: number; avgHours?: number; medianHours?: number }
export interface ApproverRow { userId: string; name: string; pending: number; oldestPendingHours?: number; decided: number }
export interface OverdueRow { requestId: string; number: string; appName: string; step: string; assignee: string; ageHours: number }
export interface Report {
  from: string; to: string; total: number; byStatus: Record<string, number>; apps: AppReportRow[]; approvers: ApproverRow[];
  daily: { day: string; count: number }[]; overdue: OverdueRow[];
}
export interface RetentionInfo { closedRequestMonths?: number; autoPurge: boolean }
export interface RetentionPreview { eligible: number; skippedAccounting: number; months?: number }
export interface AuditRow { id: number; at: string; actorId?: string; actorName?: string; action: string; objectType?: string; objectId?: string; detail?: Record<string, unknown> }
export interface AuditPage { items: AuditRow[]; total: number; page: number; pageSize: number }
export interface FormerUser { id: string; displayName: string; email: string; createdAt: string }
