import type { FlowDefinition } from './flow';
import raw from './templates.json';

export interface FlowTemplate { id: string; name: string; description: string; icon: string; definition: FlowDefinition }

/**
 * Hazır akışlar: şirketin gerçek evrakına göre uyarlanmak üzere başlangıç noktasıdır.
 * Tek kaynak `templates.json`dur; arayüz, tarayıcı/API uçtan uca betikleri ve sunucu entegrasyon testi aynı dosyayı kullanır
 * (böylece şablonların sunucu doğrulayıcısından geçtiği gerçekten sınanır).
 */
export const flowTemplates = raw as unknown as FlowTemplate[];

export const templateById = (id: string) => flowTemplates.find((t) => t.id === id);
