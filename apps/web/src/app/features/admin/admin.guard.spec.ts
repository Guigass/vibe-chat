import '@angular/compiler';
import { EnvironmentInjector, Injector, runInInjectionContext } from '@angular/core';
import { Router, UrlTree } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { AdminContextService } from './admin-context.service';
import { adminAreaGuard, adminGuard, adminLandingGuard } from './admin.guard';

function runGuard(
  guard: ReturnType<typeof adminAreaGuard> | typeof adminGuard | typeof adminLandingGuard,
  role: string | null,
): Promise<boolean | UrlTree> {
  const ensureReady = vi.fn().mockResolvedValue(undefined);
  const createUrlTree = vi.fn((commands: string[]) => ({
    commands,
  })) as unknown as Router['createUrlTree'];

  const injector = Injector.create({
    providers: [
      {
        provide: AdminContextService,
        useValue: {
          ensureReady,
          role: () => role,
        },
      },
      {
        provide: Router,
        useValue: { createUrlTree },
      },
    ],
  }) as EnvironmentInjector;

  const result = runInInjectionContext(injector, () => guard({} as never, {} as never));
  return Promise.resolve(result as boolean | UrlTree | Promise<boolean | UrlTree>);
}

describe('admin guards (BUG-005)', () => {
  it('adminGuard allows Member so the shell can show denial feedback', async () => {
    await expect(runGuard(adminGuard, 'Member')).resolves.toBe(true);
    await expect(runGuard(adminGuard, 'WorkspaceOwner')).resolves.toBe(true);
  });

  it('adminLandingGuard keeps Member on /admin and sends Owner to overview', async () => {
    await expect(runGuard(adminLandingGuard, 'Member')).resolves.toBe(true);

    const owner = await runGuard(adminLandingGuard, 'WorkspaceOwner');
    expect(owner).toEqual({ commands: ['/admin', 'overview'] });
  });

  it('adminAreaGuard sends Member to /admin instead of looping on overview', async () => {
    const result = await runGuard(adminAreaGuard('overview'), 'Member');
    expect(result).toEqual({ commands: ['/admin'] });
  });

  it('adminAreaGuard redirects Auditor away from settings to first allowed area', async () => {
    const result = await runGuard(adminAreaGuard('settings'), 'Auditor');
    expect(result).toEqual({ commands: ['/admin', 'overview'] });
  });

  it('adminAreaGuard allows Admin on settings', async () => {
    await expect(runGuard(adminAreaGuard('settings'), 'Admin')).resolves.toBe(true);
  });
});
