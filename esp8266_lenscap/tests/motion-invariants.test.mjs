import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(new URL('../esp8266_lenscap.ino', import.meta.url), 'utf8');
const startJog = source.slice(source.indexOf('bool startJog('), source.indexOf('// ==================== EEPROM'));
const openCover = source.slice(source.indexOf('void openCover('), source.indexOf('void closeCover('));
const closeCover = source.slice(source.indexOf('void closeCover('), source.indexOf('void haltCover('));
const haltStart = source.indexOf('void haltCover(');
const haltCover = source.slice(haltStart, source.indexOf('uint16_t angleToPulse(', haltStart));

assert.match(
  startJog,
  /if\s*\(isMoving\)\s*return false;/,
  'A Jog command must be rejected while open/close movement owns the servo.'
);
assert.match(openCover, /jogActive\s*=\s*false;/, 'Opening must take ownership from an active Jog.');
assert.match(closeCover, /jogActive\s*=\s*false;/, 'Closing must take ownership from an active Jog.');
assert.match(haltCover, /jogActive\s*=\s*false;/, 'Stop must cancel an active Jog.');
assert.match(source, /const uint16_t MIN_JOG_DURATION_MS\s*=\s*750;/, 'Short Jog commands need a visible minimum duration.');
assert.match(source, /jogDurationMs\s*=\s*calculatedDurationMs\s*<\s*MIN_JOG_DURATION_MS\s*\?\s*MIN_JOG_DURATION_MS/, 'The minimum duration must be enforced for every non-zero Jog.');

console.log('ESP8266 lens-cap motion invariants passed');
