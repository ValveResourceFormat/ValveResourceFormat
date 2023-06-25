import { readFileSync, writeFileSync } from 'fs';
import { dirname } from 'path';
import { execSync } from 'child_process';
import { fileURLToPath } from 'url';

const version = process.argv[ 2 ];
const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

if (!/^[0-9]+\.[0-9]+$/.test(version))
{
	console.error('Provide version as first argument. Example: 2.0');
	process.exit(1);
}

process.chdir(__dirname);

const buildPropsPath = '../Directory.Build.props';
const buildProps = readFileSync(buildPropsPath).toString();
const newBuildProps = buildProps.replace(
    /<ProjectBaseVersion>.+<\/ProjectBaseVersion>/,
    `<ProjectBaseVersion>${version}</ProjectBaseVersion>`
);

writeFileSync(buildPropsPath, newBuildProps);

execSync(`git add ${buildPropsPath} && git commit --message="Bump version to ${version}"`, { stdio: 'inherit' });
execSync(`git tag "${version}" --message="${version}" --sign`, { stdio: 'inherit' });
