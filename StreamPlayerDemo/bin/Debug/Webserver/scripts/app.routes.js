(function () {
    angular.module('app.routes', ['ngRoute'])

        .config(['$routeProvider', '$locationProvider', function($routeProvider) {
            $routeProvider.
                when('/', {
                    templateUrl: '/views/home.html',
                    title: 'Home'
                }).
                when('/people', {
                    templateUrl: '/views/people.html',
                    title: 'People'
                }).
                when('/info', {
                    templateUrl: '/views/info.html',
                    title: 'Camera System Status Info'
                }).
                when('/cmd', {
                    templateUrl: '/views/cmd.html',
                    title: 'Command-Line Interface'
                }).
                when('/tubular', {
                    templateUrl: '/views/tubular.html',
                    title: 'Tubular Grid'
                }).
                otherwise({
                    redirectTo: '/'
                });
        }]);
})();