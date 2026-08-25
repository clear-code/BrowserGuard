'use strict';

// Reading one setting out of a config section.
//
// A section that says nothing about a setting, or says it in the wrong shape,
// leaves the value as it was. Written out at each call site this is three lines
// of type check per setting, and a setting whose check is forgotten takes
// undefined instead: the feature then behaves as though it had been turned off,
// with nothing to say so.

export function readBoolean(section, key, current) {
  return typeof section?.[key] === 'boolean' ? section[key] : current;
}

export function readNumber(section, key, current) {
  return Number.isFinite(section?.[key]) ? section[key] : current;
}

export function readArray(section, key, current) {
  return Array.isArray(section?.[key]) ? section[key] : current;
}
