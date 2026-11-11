import { carpetaLegible, nombreLegible } from './nombre-legible';

describe('nombreLegible', () => {
  it.each([
    ['CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf', 'CV · NET · MarcoTalento'],
    [
      'CV_Eduardo_Lara_Navarro_Java_Angular_ClubIbericoLibro.docx',
      'CV · Java · Angular · ClubIbericoLibro',
    ],
    ['Carta_Presentacion_Eduardo_Lara_Navarro_ABM.pdf', 'Carta · ABM'],
    ['Carta_Motivacion_Eduardo_Lara_Navarro_nomadix.pdf', 'Carta de motivación · nomadix'],
    ['Dosier_Competencias_NovaIT_Eduardo_Lara_Navarro.pdf', 'Dosier · Competencias · NovaIT'],
    // El de la raíz de Curriculum, que no es para ninguna oferta
    ['CV_Eduardo_Lara_Navarro.pdf', 'CV general'],
    ['CV_Eduardo_Lara_Navarro.docx', 'CV general'],
    ['Carta_Presentacion_Eduardo_Lara_Navarro.pdf', 'Carta general'],
    ['CV_Eduardo_Lara_Navarro_BASE.docx', 'CV · BASE'],
    ['CV Eduardo 2025.pdf', 'CV Eduardo 2025'],
    ['CV_Ejemplo_Persona_NET_EmpresaEjemplo.pdf', 'CV · Ejemplo · Persona · NET · EmpresaEjemplo'],
  ])('%s → %s', (archivo, esperado) => {
    expect(nombreLegible(archivo)).toBe(esperado);
  });

  it('si no queda nada, devuelve el nombre tal cual', () => {
    expect(nombreLegible('_.pdf')).toBe('_.pdf');
  });
});

describe('carpetaLegible', () => {
  it.each([
    ['Candidaturas/Semana 2026-08-31 (31 ago - 6 sep)', '31 ago - 6 sep'],
    ['Candidaturas/Semana 2026-11-02 (2 - 8 nov)', '2 - 8 nov'],
    ['Archivo/_estilo_antiguo', 'Archivo · estilo antiguo'],
    ['Archivo/Antiguos', 'Archivo · Antiguos'],
    ['Archivo', 'Archivo'],
    ['', 'Carpeta Curriculum'],
    ['Candidaturas/Otra', 'Otra'],
  ])('%s → %s', (carpeta, esperado) => {
    expect(carpetaLegible(carpeta)).toBe(esperado);
  });
});
