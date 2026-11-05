import { accountingStatusLabel, accountingTone, requestStatusLabel, requestTone, taskStatusLabel, taskTone, type Tone } from '../lib/requestText';
import type { AccountingEntry, RequestStatus, TaskStatus } from '../types';

const icon: Record<Tone, string> = { ok: '✓', bad: '✕', warn: '◔', info: '◔', neutral: '–' };

function Badge({ tone, label }: { tone: Tone; label: string }) {
  return <span className={`badge ${tone}`}>{icon[tone]} {label}</span>;
}

export const RequestBadge = ({ status }: { status: RequestStatus }) => <Badge tone={requestTone(status)} label={requestStatusLabel[status]} />;
export const TaskBadge = ({ status }: { status: TaskStatus }) => <Badge tone={taskTone(status)} label={taskStatusLabel[status]} />;
export const AccountingBadge = ({ status }: { status: AccountingEntry['status'] }) => <Badge tone={accountingTone(status)} label={accountingStatusLabel[status]} />;
