const pages = { Work: ['Project', 'Notes', 'References'], Study: ['Courses', 'Exercises', 'Reading'], Personal: ['Photos', 'Ideas', 'Plans'] };
document.querySelectorAll('[data-page]').forEach(button => {
  button.addEventListener('click', () => {
    document.querySelectorAll('[data-page]').forEach(item => item.setAttribute('aria-pressed', String(item === button)));
    document.querySelectorAll('#desktop .icon').forEach((icon, index) => { icon.replaceChildren(Object.assign(document.createElement('span'), { ariaHidden: 'true' }), document.createTextNode(pages[button.dataset.page][index])); });
    document.getElementById('demo-caption').textContent = `${button.dataset.page} page · Concept illustration, not an app screenshot.`;
  });
});
