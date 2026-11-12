import type { TreeDirectoryDto, TreeGroupDto, TreeSearchMatchDto, TreeSessionDto } from '../api/types';

// Projects the full tree through a tree.search result: only matches, their ancestors (always expanded) and, for
// supervisors with matching workers, those workers nested. Containers and sessions are ordered by match rank
// (rank = index in `matches`; a container ranks by its best descendant).

export interface ProjSession { kind: 'session'; dto: TreeSessionDto; rank: number; workers: TreeSessionDto[] }
export interface ProjGroup { kind: 'group'; id: string; name: string; rank: number; children: ProjChild[] }
export interface ProjDirectory { kind: 'directory'; id: string; label: string; rank: number; children: ProjChild[] }
export type ProjChild = ProjGroup | ProjSession;

export interface Projection {
	directories: ProjDirectory[];
	/** Supervisors that have matching workers whose rows are not loaded yet (caller loads via tree.workers). */
	needWorkers: string[];
}

export function projectSearch(
	directories: TreeDirectoryDto[],
	workers: Record<string, TreeSessionDto[]>,
	matches: TreeSearchMatchDto[],
): Projection {
	const rankById = new Map<string, number>();
	const matchById = new Map<string, TreeSearchMatchDto>();
	matches.forEach((m, i) => { rankById.set(m.id, i); matchById.set(m.id, m); });
	const needWorkers: string[] = [];

	const projectSession = (s: TreeSessionDto): ProjSession | null => {
		const rank = rankById.get(s.id);
		if (rank === undefined) return null;
		const m = matchById.get(s.id)!;
		let ws: TreeSessionDto[] = [];
		if (m.workerIds.length > 0) {
			const loaded = workers[s.id];
			if (loaded) {
				const byId = new Map(loaded.map((w) => [w.id, w]));
				ws = m.workerIds.map((id) => byId.get(id)).filter((w): w is TreeSessionDto => !!w);
			} else {
				needWorkers.push(s.id);
			}
		}
		return { kind: 'session', dto: s, rank, workers: ws };
	};

	const projectContainer = (groups: TreeGroupDto[], sessions: TreeSessionDto[]): { children: ProjChild[]; rank: number } => {
		const children: ProjChild[] = [];
		for (const g of groups) {
			const pg = projectGroup(g);
			if (pg) children.push(pg);
		}
		for (const s of sessions) {
			const ps = projectSession(s);
			if (ps) children.push(ps);
		}
		// Array.sort is stable: ties keep model order (groups first, then sessions).
		children.sort((a, b) => a.rank - b.rank);
		return { children, rank: children.length > 0 ? children[0].rank : Number.POSITIVE_INFINITY };
	};

	const projectGroup = (g: TreeGroupDto): ProjGroup | null => {
		const { children, rank } = projectContainer(g.groups, g.sessions);
		if (children.length === 0) return null;
		return { kind: 'group', id: g.id, name: g.name, rank, children };
	};

	const result: ProjDirectory[] = [];
	for (const d of directories) {
		const { children, rank } = projectContainer(d.groups, d.sessions);
		if (children.length === 0) continue;
		result.push({ kind: 'directory', id: d.id, label: d.label, rank, children });
	}
	result.sort((a, b) => a.rank - b.rank);
	return { directories: result, needWorkers };
}
