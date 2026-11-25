// @ts-check
// JSDoc shapes of the API responses the converted pages read. Types only: nothing here runs.
// Fields are the ones the pages use, not the full server records.

/**
 * @typedef {object} LiveState  Cockpit.live()'s onState payload.
 * @property {boolean} connected
 * @property {boolean} polling
 * @property {string} label
 * @property {number | null} lastUpdated
 */

/**
 * @typedef {object} CommandRef
 * @property {string} id
 * @property {string} title
 * @property {string} status
 */

/**
 * @typedef {object} AttentionItem
 * @property {string} id
 * @property {string} title
 * @property {string} status
 * @property {string} reason
 */

/**
 * @typedef {object} ProjectSummary  GET /api/projects/{name}, and each row of GET /api/projects.
 * @property {string} name
 * @property {boolean} exists
 * @property {Record<string, number>} counts
 * @property {boolean} watcherAlive
 * @property {CommandRef | null} running
 * @property {CommandRef | null} latest
 * @property {AttentionItem[]} attention
 */

/**
 * @typedef {object} Overview  GET /api/overview?project=
 * @property {boolean} chatEnabled
 * @property {string[]} projects
 * @property {ProjectSummary | null} project
 */

/**
 * @typedef {object} LaneCommand  GET /api/projects/{name}/commands, one row.
 * @property {string} id
 * @property {string} title
 * @property {string} status
 * @property {boolean} malformed
 * @property {boolean} isFollowUp
 * @property {string | null} summary
 * @property {number} attempts
 * @property {number | null} maxAttempts
 * @property {number | null} durationSeconds
 * @property {string | null} branch
 */

/**
 * @typedef {object} GitCommit
 * @property {string} hash
 * @property {string} message
 * @property {string} author
 * @property {string} date
 */

/**
 * @typedef {object} GitInfo  GET /api/projects/{name}/git
 * @property {boolean} available
 * @property {string | null} branch
 * @property {boolean} dirty
 * @property {number | null} ahead
 * @property {number | null} behind
 * @property {GitCommit[]} commits
 * @property {string | null} defaultCommitMessage
 */

/**
 * @typedef {object} GitBranches  GET /api/projects/{name}/git/branches[?fetch=true]
 * @property {{name: string, remoteOnly: boolean}[]} branches
 * @property {string | null} current
 * @property {string | null} fetchError
 */

/**
 * @typedef {object} ProcessResult  A write that shelled out: git pull/sync/checkout, mf-run.
 * @property {boolean} success
 * @property {number} exitCode
 * @property {boolean} timedOut
 * @property {string} output
 * @property {string[]} [conflicts]
 * @property {ProcessResult | null} [push]
 */

/**
 * @typedef {object} CommitAllResult  POST /api/projects/{name}/git/commit-all
 * @property {boolean} committed
 * @property {ProcessResult | null} push
 */

/**
 * @typedef {object} RunService  One service of GET /api/projects/{name}/run.
 * @property {string} name
 * @property {boolean} running
 * @property {number | null} pid
 * @property {boolean | null} portListening
 * @property {string | null} url
 * @property {string | null} reason
 */

/**
 * @typedef {RunService & {
 *   state: string | null, sha: string | null, dirty: boolean, at: string | null,
 *   message: string | null, link: string | null
 * }} StableService  The stable instance's row.
 */

/**
 * @typedef {object} RunStatus  GET /api/projects/{name}/run
 * @property {boolean} configured
 * @property {RunService[]} services
 * @property {StableService | null} stable
 * @property {string | null} head
 * @property {string | null} error
 */

/**
 * @typedef {object} LogTail  GET .../run/{service}/log, .../actions/{name}/log
 * @property {boolean} exists
 * @property {string[]} lines
 */

/**
 * @typedef {object} WatchStatus  GET /api/projects/{name}/watch?tail=100
 * @property {boolean} running
 * @property {LogTail} log
 */

/**
 * @typedef {object} ActionLast
 * @property {string | null} runAt
 * @property {string | null} trigger
 * @property {string | null} result
 * @property {number | null} durationSeconds
 * @property {string | null} refusedAt
 * @property {string | null} refused
 * @property {string | null} skippedAt
 * @property {string | null} skipped
 */

/**
 * @typedef {object} Action
 * @property {string} name
 * @property {string | null} description
 * @property {string} schedule
 * @property {boolean} pending
 * @property {ActionLast | null} last
 */

/**
 * @typedef {object} ActionsStatus  GET /api/projects/{name}/actions
 * @property {boolean} exists
 * @property {boolean} ignored
 * @property {string | null} error
 * @property {Action[]} actions
 */

/**
 * @typedef {object} NewProjectInfo  GET /api/new-project
 * @property {string | null} root
 * @property {string[]} templates
 * @property {boolean} specKit
 * @property {string} separator
 */

/**
 * @typedef {object} BrowseListing  GET /api/browse
 * @property {string} path
 * @property {string | null} parent
 * @property {string[]} roots
 * @property {{name: string, path: string, git: boolean}[]} dirs
 */

/**
 * @typedef {object} AddProjectResult  POST /api/projects (201), and the 422 error body.
 * @property {string} name
 * @property {string[]} [warnings]
 * @property {string} [path]
 * @property {string} [output]
 * @property {string} [error]
 */

export {};
