import { describe, expect, it } from 'vitest';
import { logoFill, luminance, techLogoIds } from './techLogo';

describe('techLogoIds', () => {
  it('maps framework names from the sample model to brand logos', () => {
    expect(techLogoIds('ASP.NET Core Web API')).toEqual(['dotnet']);
    expect(techLogoIds('ASP.NET Core Worker Service')).toEqual(['dotnet']);
    expect(techLogoIds('PostgreSQL')).toEqual(['postgresql']);
    expect(techLogoIds('React, TypeScript')).toEqual(['react', 'typescript']);
  });

  it('prefers the specific brand over the generic one at the same position', () => {
    expect(techLogoIds('Spring Boot + JPA')).toEqual(['springboot', 'openjdk']);
    expect(techLogoIds('Next.js')).toEqual(['nextjs']);
    expect(techLogoIds('Node.js ve Vue.js')).toEqual(['nodejs', 'vuejs']);
    expect(techLogoIds('Kubernetes (k8s) üzerinde Docker')).toEqual(['kubernetes', 'docker']);
  });

  it('does not invent logos for plain words and honours the limit', () => {
    expect(techLogoIds('Nesne depolama')).toEqual([]);
    expect(techLogoIds('Gitmek istiyorum')).toEqual([]);
    expect(techLogoIds('React, TypeScript, PostgreSQL, Redis, Docker', 2)).toHaveLength(2);
    expect(techLogoIds(null)).toEqual([]);
  });
});

describe('logo contrast', () => {
  it('falls back to the text colour when a brand colour would vanish on the surface', () => {
    expect(logoFill('#000000', true)).toBe('currentColor');
    expect(logoFill('#000000', false)).toBe('#000000');
    expect(logoFill('#F7DF1E', false)).toBe('#F7DF1E');        // JavaScript sarısı açık zeminde de görünür
    expect(logoFill('#FFFFFF', false)).toBe('currentColor');
    expect(logoFill('#326CE5', true)).toBe('#326CE5');
  });

  it('computes relative luminance', () => {
    expect(luminance('#000000')).toBeCloseTo(0, 5);
    expect(luminance('#ffffff')).toBeCloseTo(1, 5);
  });
});
