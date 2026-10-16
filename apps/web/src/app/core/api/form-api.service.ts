import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';

export type FormStatus = 'draft' | 'published' | 'archived';
export type FormFieldType = 'text' | 'number' | 'choice' | 'date' | 'user' | 'attachment';
export type FormApprovalMode = 'single' | 'n_of_m';
export type FormResult = 'none' | 'message' | 'task';
export type FormSubmissionState = 'pending' | 'approved' | 'rejected';

export interface FormField {
  key: string;
  type: FormFieldType;
  label: string;
  required: boolean;
  hidden: boolean;
  options: string[];
}

export interface FormSchema {
  contractVersion: 'form.schema.v1';
  fields: FormField[];
  approval: { mode: FormApprovalMode; required: number; reviewerUserIds: string[] };
  onApproved: FormResult;
}

export interface FormListItem {
  id: string;
  channelId: string;
  title: string;
  status: FormStatus;
  publishedVersion: number;
  updatedAt: string;
}

export interface FormDetail extends FormListItem {
  canEdit: boolean;
  canSubmit: boolean;
  canReview: boolean;
  draftSchema: FormSchema | null;
  publishedSchema: FormSchema | null;
}

export interface FormDecision {
  version: number;
  actorUserId: string;
  decision: 'approve' | 'reject';
  createdAt: string;
}

export interface FormSubmission {
  id: string;
  formId: string;
  schemaVersion: number;
  state: FormSubmissionState;
  answersPurged: boolean;
  answers: Record<string, string | number> | null;
  submittedBy: string;
  submittedAt: string;
  canDecide: boolean;
  resultMessageId: string | null;
  resultTaskId: string | null;
  decisions: FormDecision[];
}

@Injectable({ providedIn: 'root' })
export class FormApiService extends HttpApiClient {
  list(workspaceId: string, channelId: string): Promise<FormListItem[]> {
    return this.request<{ items: FormListItem[] }>(
      `/api/v1/workspaces/${workspaceId}/forms?channelId=${channelId}`,
    ).then((page) => page.items ?? []);
  }

  get(workspaceId: string, formId: string): Promise<FormDetail> {
    return this.request<FormDetail>(`/api/v1/workspaces/${workspaceId}/forms/${formId}`);
  }

  create(workspaceId: string, body: {
    idempotencyKey: string;
    channelId: string;
    title: string;
    schema: FormSchema;
  }): Promise<FormDetail> {
    return this.request<FormDetail>(`/api/v1/workspaces/${workspaceId}/forms`, {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  update(workspaceId: string, formId: string, body: { title: string; schema: FormSchema }): Promise<FormDetail> {
    return this.request<FormDetail>(`/api/v1/workspaces/${workspaceId}/forms/${formId}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    });
  }

  publish(workspaceId: string, formId: string): Promise<FormDetail> {
    return this.request<FormDetail>(`/api/v1/workspaces/${workspaceId}/forms/${formId}/publish`, { method: 'POST' });
  }

  archive(workspaceId: string, formId: string): Promise<FormDetail> {
    return this.request<FormDetail>(`/api/v1/workspaces/${workspaceId}/forms/${formId}/archive`, { method: 'POST' });
  }

  submissions(workspaceId: string, formId: string): Promise<FormSubmission[]> {
    return this.request<{ items: FormSubmission[] }>(
      `/api/v1/workspaces/${workspaceId}/forms/${formId}/submissions`,
    ).then((page) => page.items ?? []);
  }

  submit(workspaceId: string, formId: string, body: {
    idempotencyKey: string;
    answers: Record<string, string | number>;
  }): Promise<FormSubmission> {
    return this.request<FormSubmission>(`/api/v1/workspaces/${workspaceId}/forms/${formId}/submissions`, {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  decide(workspaceId: string, formId: string, submissionId: string, body: {
    idempotencyKey: string;
    decision: 'approve' | 'reject';
  }): Promise<FormSubmission> {
    return this.request<FormSubmission>(
      `/api/v1/workspaces/${workspaceId}/forms/${formId}/submissions/${submissionId}/decisions`,
      { method: 'POST', body: JSON.stringify(body) },
    );
  }
}
