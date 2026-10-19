import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const errors = {
  REVIEW_RESULTS_INVALID: {
    code: 400,
    status: 'INVALID_ARGUMENT',
    message: 'Provide passing results for every enabled review lens, meeting its threshold and naming the reviewed HEAD and reviewer.',
  },
  REVIEW_SETUP_FAILURE: {
    code: 500,
    status: 'INTERNAL',
    message: 'The review harness could not run. Check the repository configuration and installed packages.',
  },
};

export const validateResults = (results, agents, head) => {
  const required = agents.filter((agent) => agent.enabled !== false);
  if (!results || typeof results !== 'object' || Array.isArray(results) || required.length === 0 ||
      Object.keys(results).length !== required.length || required.some((agent) => {
        const result = results[agent.name];
        return !result || result.verdict !== 'PASS' || !Number.isFinite(result.score) ||
          result.score < agent.threshold || result.score > 10 || result.reviewedCommit !== head ||
          typeof result.reviewer !== 'string' || result.reviewer.trim().length === 0;
      })) {
    throw new Error('REVIEW_RESULTS_INVALID');
  }
  return results;
};
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  let viewDirectory;
  let temporaryRoot;

  try {
    const action = process.argv[2] ?? 'gate';
    const entries = { gate: 'review-gate.js', info: 'review-info.js', attest: 'review-attest.js' };
    if (!Object.hasOwn(entries, action)) {
      throw new Error('Unsupported review action');
    }
    const configuration = JSON.parse(readFileSync('.claude/review.config.json', 'utf8'));
    if (configuration.base !== 'origin/main') {
      throw new Error('The supported review base is origin/main');
    }
    const repositoryRoot = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
    if (realpathSync(repositoryRoot) !== realpathSync(process.cwd())) {
      throw new Error('Run the review command from its repository root');
    }
    const head = execFileSync('git', ['rev-parse', 'HEAD^{commit}'], { encoding: 'utf8' }).trim();
    const base = execFileSync('git', ['rev-parse', 'origin/main^{commit}'], { encoding: 'utf8' }).trim();
    const commonDirectory = execFileSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { encoding: 'utf8' }).trim();
    mkdirSync(join(repositoryRoot, '.review', 'work'), { recursive: true });
    temporaryRoot = realpathSync(join(repositoryRoot, '.review', 'work'));
    viewDirectory = mkdtempSync(join(temporaryRoot, 'review-'));
    execFileSync('git', ['init', '--bare', '--quiet', viewDirectory]);
    writeFileSync(join(viewDirectory, 'objects', 'info', 'alternates'), `${join(commonDirectory, 'objects').replaceAll('\\', '/')}\n`);
    const environment = { ...process.env, GIT_DIR: viewDirectory, GIT_WORK_TREE: repositoryRoot, GIT_CONFIG_COUNT: '0' };
    delete environment.GIT_COMMON_DIR;
    delete environment.GIT_INDEX_FILE;
    execFileSync('git', ['update-ref', 'refs/heads/review', head], { env: environment });
    execFileSync('git', ['update-ref', 'refs/remotes/origin/main', base], { env: environment });
    execFileSync('git', ['symbolic-ref', 'HEAD', 'refs/heads/review'], { env: environment });
    execFileSync('git', ['config', 'remote.origin.url', '.'], { env: environment });
    execFileSync('git', ['config', 'remote.origin.fetch', '+refs/heads/*:refs/remotes/origin/*'], { env: environment });
    execFileSync('git', ['config', 'branch.review.remote', 'origin'], { env: environment });
    execFileSync('git', ['config', 'branch.review.merge', 'refs/heads/main'], { env: environment });
    const parameters = action === 'attest'
      ? [JSON.stringify(validateResults(JSON.parse(readFileSync(process.argv[3], 'utf8')), configuration.agents, head))]
      : process.argv.slice(3);
    const entry = fileURLToPath(new URL(`../node_modules/bladeforge-review-harness/dist/${entries[action]}`, import.meta.url));
    const result = spawnSync(process.execPath, [entry, ...parameters], { env: environment, stdio: 'inherit' });
    if (result.error) {
      throw result.error;
    }
    process.exitCode = result.status ?? 1;
  } catch (error) {
    const reason = error.message === 'REVIEW_RESULTS_INVALID' ? error.message : 'REVIEW_SETUP_FAILURE';
    process.stderr.write(`${JSON.stringify({ error: { ...errors[reason], details: [{ '@type': 'ErrorInfo', reason, domain: 'sts2.review' }] } })}\n`);
    process.exitCode = 1;
  } finally {
    if (viewDirectory && temporaryRoot && resolve(viewDirectory).startsWith(`${resolve(temporaryRoot)}${sep}`)) {
      rmSync(viewDirectory, { recursive: true, force: true });
    }
  }

}
