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
export interface WorkspaceInfo { id: string; name: string; role: string; canAccept: boolean }
export interface AuthResult {
  token: string; user: { id: string; email: string; displayName: string }; workspaces: WorkspaceInfo[];
}
export interface Artifact { id: string; versionNumber: number; format: string; fileName: string; contentHash: string; modelHash: string; size: number }
